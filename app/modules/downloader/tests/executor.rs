//! End-to-end executor tests against a loopback-only HTTP server. No external
//! network or test-server crate is used.

use std::collections::{BTreeMap, HashMap};
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use archoera_dl::{
    CancellationToken, DownloadPlan, FileMode, FileSpec, HashSpec, PlanOptions, Progress,
    VerifyExisting, execute_plan, validate_plan,
};
use sha1::{Digest, Sha1};
use tempfile::TempDir;

struct TestServer {
    address: std::net::SocketAddr,
    stop: Arc<AtomicBool>,
    handle: Option<thread::JoinHandle<()>>,
}

#[derive(Clone)]
struct Route {
    status: u16,
    body: Vec<u8>,
    omit_length: bool,
    slow: Option<Duration>,
}

impl Route {
    fn ok(body: impl Into<Vec<u8>>) -> Self {
        Self {
            status: 200,
            body: body.into(),
            omit_length: false,
            slow: None,
        }
    }

    fn status(status: u16) -> Self {
        Self {
            status,
            body: b"error".to_vec(),
            omit_length: false,
            slow: None,
        }
    }

    fn truncated(body: impl Into<Vec<u8>>) -> Self {
        Self {
            status: 200,
            body: body.into(),
            omit_length: true,
            slow: None,
        }
    }

    fn slow(body: impl Into<Vec<u8>>, delay: Duration) -> Self {
        Self {
            status: 200,
            body: body.into(),
            omit_length: false,
            slow: Some(delay),
        }
    }
}

impl TestServer {
    fn start(routes: HashMap<String, Route>) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind loopback");
        let address = listener.local_addr().expect("local addr");
        let stop = Arc::new(AtomicBool::new(false));
        let stop_thread = Arc::clone(&stop);
        let routes = Arc::new(routes);
        let handle = thread::spawn(move || {
            while let Ok((stream, _)) = listener.accept() {
                if stop_thread.load(Ordering::SeqCst) {
                    break;
                }
                let routes = Arc::clone(&routes);
                thread::spawn(move || serve_connection(stream, &routes));
            }
        });
        Self {
            address,
            stop,
            handle: Some(handle),
        }
    }

    fn url(&self, path: &str) -> String {
        format!("http://{}{}", self.address, path)
    }
}

impl Drop for TestServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::SeqCst);
        let _ = TcpStream::connect(self.address);
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}

fn serve_connection(mut stream: TcpStream, routes: &HashMap<String, Route>) {
    let _ = stream.set_read_timeout(Some(Duration::from_secs(2)));
    let _ = stream.set_nodelay(true);
    let mut request = Vec::new();
    let mut buffer = [0_u8; 2048];
    loop {
        match stream.read(&mut buffer) {
            Ok(0) => return,
            Ok(read) => {
                request.extend_from_slice(&buffer[..read]);
                if request.windows(4).any(|window| window == b"\r\n\r\n") {
                    break;
                }
                if request.len() > 16 * 1024 {
                    return;
                }
            }
            Err(_) => return,
        }
    }

    let text = String::from_utf8_lossy(&request);
    let path = text
        .lines()
        .next()
        .and_then(|line| line.split_whitespace().nth(1))
        .unwrap_or("/");
    let route = routes
        .get(path)
        .cloned()
        .unwrap_or_else(|| Route::status(404));
    let reason = match route.status {
        200 => "OK",
        404 => "Not Found",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => "Status",
    };

    let mut headers = format!(
        "HTTP/1.1 {} {}\r\nConnection: close\r\nContent-Type: application/octet-stream\r\n",
        route.status, reason
    );
    if !route.omit_length {
        headers.push_str(&format!("Content-Length: {}\r\n", route.body.len()));
    }
    headers.push_str("\r\n");
    if stream.write_all(headers.as_bytes()).is_err() {
        return;
    }

    if let Some(delay) = route.slow {
        for chunk in route.body.chunks(4096) {
            if stream.write_all(chunk).is_err() {
                return;
            }
            if stream.flush().is_err() {
                return;
            }
            thread::sleep(delay);
        }
    } else {
        let _ = stream.write_all(&route.body);
    }
    let _ = stream.flush();
}

fn sha1_hex(bytes: &[u8]) -> String {
    let digest = Sha1::digest(bytes);
    let mut output = String::with_capacity(40);
    for byte in digest {
        output.push_str(&format!("{byte:02x}"));
    }
    output
}

fn file(url: String, body: &[u8], dest: &str) -> FileSpec {
    FileSpec {
        url,
        mirrors: Vec::new(),
        hash: Some(HashSpec {
            algo: "sha1".into(),
            hex: sha1_hex(body),
        }),
        sha1: None,
        sha256: None,
        sha512: None,
        size: body.len() as u64,
        dest: dest.into(),
        mode: FileMode::Ensure,
        priority: 0,
        executable: false,
        headers: BTreeMap::new(),
    }
}

fn mirror(mut spec: FileSpec, url: String) -> FileSpec {
    spec.mirrors.push(url);
    spec
}

fn plan(root: &Path, files: Vec<FileSpec>) -> DownloadPlan {
    DownloadPlan {
        schema_version: 1,
        plan_id: "test-plan".into(),
        root: root.display().to_string(),
        concurrency: 4,
        priority: 0,
        bandwidth_limit: 0,
        total_bytes: 0,
        options: PlanOptions {
            progress_interval_ms: 20,
            ..PlanOptions::default()
        },
        files,
        post: Vec::new(),
    }
}

fn read(path: &Path) -> Vec<u8> {
    std::fs::read(path).expect("read downloaded file")
}

fn assert_no_parts(root: &Path) {
    let mut stack = vec![root.to_path_buf()];
    while let Some(directory) = stack.pop() {
        for entry in std::fs::read_dir(&directory).expect("read_dir") {
            let entry = entry.expect("dir entry");
            let path = entry.path();
            if entry.file_type().expect("file type").is_dir() {
                stack.push(path);
            } else {
                let name = path
                    .file_name()
                    .and_then(|name| name.to_str())
                    .unwrap_or("");
                assert!(
                    !name.ends_with(".part"),
                    "leftover part file: {}",
                    path.display()
                );
            }
        }
    }
}

fn capture_progress() -> (Arc<Mutex<Vec<Progress>>>, archoera_dl::ProgressCallback) {
    let events = Arc::new(Mutex::new(Vec::new()));
    let sink = Arc::clone(&events);
    let callback: archoera_dl::ProgressCallback =
        Arc::new(move |progress| sink.lock().expect("progress lock").push(progress));
    (events, callback)
}

#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn downloads_three_files_with_mirror_fallback_and_progress() {
    let temp = TempDir::new().expect("tempdir");
    let first = b"first file contents".to_vec();
    let second = b"second file via mirror".to_vec();
    let third = b"third file contents".to_vec();

    let mut routes = HashMap::new();
    routes.insert("/a".into(), Route::ok(first.clone()));
    routes.insert("/b-primary".into(), Route::status(404));
    routes.insert("/b-mirror".into(), Route::ok(second.clone()));
    routes.insert("/c".into(), Route::ok(third.clone()));
    let server = TestServer::start(routes);

    let files = vec![
        file(server.url("/a"), &first, "a/first.bin"),
        mirror(
            file(server.url("/b-primary"), &second, "b/second.bin"),
            server.url("/b-mirror"),
        ),
        file(server.url("/c"), &third, "c/third.bin"),
    ];
    let (events, callback) = capture_progress();
    let result = execute_plan(
        plan(temp.path(), files),
        CancellationToken::new(),
        Some(callback),
    )
    .await
    .expect("execute");

    assert_eq!(result.status.as_str(), "completed");
    assert_eq!(result.ok, 3);
    assert_eq!(result.skipped, 0);
    assert!(result.failed.is_empty(), "{:?}", result.failed);
    assert_eq!(
        result.bytes,
        (first.len() + second.len() + third.len()) as u64
    );
    assert!(result.post.is_empty());

    assert_eq!(read(&temp.path().join("a/first.bin")), first);
    assert_eq!(read(&temp.path().join("b/second.bin")), second);
    assert_eq!(read(&temp.path().join("c/third.bin")), third);
    assert_no_parts(temp.path());

    let events = events.lock().expect("events");
    assert!(!events.is_empty(), "progress callback must fire");
    let mut previous_bytes = 0;
    let mut previous_files = 0;
    for event in events.iter() {
        assert!(
            event.done_bytes >= previous_bytes,
            "done_bytes must be monotonic"
        );
        assert!(
            event.files_done >= previous_files,
            "files_done must be monotonic"
        );
        assert!(event.done_bytes <= event.total_bytes);
        previous_bytes = event.done_bytes;
        previous_files = event.files_done;
    }
    let last = events.last().expect("final event");
    assert_eq!(last.done_bytes, result.bytes);
    assert_eq!(last.files_done, 3);
    assert_eq!(last.files_total, 3);

    let json: serde_json::Value =
        serde_json::from_str(&result.to_json().expect("result JSON")).expect("valid JSON");
    assert_eq!(json["planId"], "test-plan");
    assert_eq!(json["status"], "completed");
    assert!(json["elapsedMs"].is_u64());
    assert!(json["post"].as_array().expect("post array").is_empty());
}

#[tokio::test]
async fn skips_existing_file_with_matching_size() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"already present".to_vec();
    let dest = temp.path().join("existing.bin");
    std::fs::write(&dest, &body).expect("seed dest");

    let mut routes = HashMap::new();
    routes.insert("/never".into(), Route::status(404));
    let server = TestServer::start(routes);

    let result = execute_plan(
        plan(
            temp.path(),
            vec![file(server.url("/never"), &body, "existing.bin")],
        ),
        CancellationToken::new(),
        None,
    )
    .await
    .expect("execute");

    assert_eq!(result.status.as_str(), "completed");
    assert_eq!(result.ok, 0);
    assert_eq!(result.skipped, 1);
    assert_eq!(read(&dest), body);
}

#[tokio::test]
async fn verify_existing_hash_redownloads_corrupt_same_size_file() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"correct payload".to_vec();
    let corrupt = b"corrupt payload".to_vec();
    assert_eq!(body.len(), corrupt.len());
    let dest = temp.path().join("verified.bin");
    std::fs::write(&dest, &corrupt).expect("seed corrupt dest");

    let mut routes = HashMap::new();
    routes.insert("/verified".into(), Route::ok(body.clone()));
    let server = TestServer::start(routes);

    let mut download = file(server.url("/verified"), &body, "verified.bin");
    let mut request = plan(temp.path(), vec![download.clone()]);
    request.options.verify_existing = VerifyExisting::Hash;

    let result = execute_plan(request, CancellationToken::new(), None)
        .await
        .expect("execute");
    assert_eq!(result.status.as_str(), "completed");
    assert_eq!(result.ok, 1);
    assert_eq!(read(&dest), body);

    // And a matching hash is skipped without downloading.
    download.size = body.len() as u64;
    let second = execute_plan(
        DownloadPlan {
            options: PlanOptions {
                verify_existing: VerifyExisting::Hash,
                ..PlanOptions::default()
            },
            ..plan(temp.path(), vec![download])
        },
        CancellationToken::new(),
        None,
    )
    .await
    .expect("execute");
    assert_eq!(second.skipped, 1);
}

#[tokio::test]
async fn rejects_parent_escape_before_writing() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"escape".to_vec();
    let request = plan(
        temp.path(),
        vec![file(
            "http://127.0.0.1:9/escape".into(),
            &body,
            "../escape.bin",
        )],
    );
    let error = execute_plan(request, CancellationToken::new(), None)
        .await
        .expect_err("must reject");
    assert_eq!(error.code().as_str(), "PATH_ESCAPE");
    assert!(
        !temp
            .path()
            .parent()
            .expect("parent")
            .join("escape.bin")
            .exists()
    );
}

#[tokio::test]
async fn rejects_duplicate_destinations() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"duplicate".to_vec();
    let request = plan(
        temp.path(),
        vec![
            file("http://127.0.0.1:9/one".into(), &body, "same.bin"),
            file("http://127.0.0.1:9/two".into(), &body, "same.bin"),
        ],
    );
    let error = execute_plan(request, CancellationToken::new(), None)
        .await
        .expect_err("must reject");
    assert_eq!(error.code().as_str(), "DUPLICATE_DEST");
    assert!(validate_plan(&plan(temp.path(), Vec::new())).is_ok());
}

#[tokio::test]
async fn wrong_hash_is_reported_and_part_is_removed() {
    let temp = TempDir::new().expect("tempdir");
    let served = b"actual bytes".to_vec();
    let expected = b"expected1234".to_vec();
    assert_eq!(served.len(), expected.len());

    let mut routes = HashMap::new();
    routes.insert("/bad-hash".into(), Route::ok(served.clone()));
    let server = TestServer::start(routes);

    let result = execute_plan(
        plan(
            temp.path(),
            vec![file(server.url("/bad-hash"), &expected, "bad.bin")],
        ),
        CancellationToken::new(),
        None,
    )
    .await
    .expect("execute");

    assert_eq!(result.status.as_str(), "failed");
    assert_eq!(result.ok, 0);
    assert_eq!(result.failed.len(), 1);
    assert_eq!(result.failed[0].code, "HASH_MISMATCH");
    assert!(!result.failed[0].retryable);
    assert!(!temp.path().join("bad.bin").exists());
    assert_no_parts(temp.path());
}

#[tokio::test]
async fn truncated_body_is_size_mismatch() {
    let temp = TempDir::new().expect("tempdir");
    let served = b"short".to_vec();
    let expected_size = 128_u64;
    let mut spec = file(
        "http://127.0.0.1:9/truncated".into(),
        b"0123456789abcdef",
        "truncated.bin",
    );
    spec.size = expected_size;

    let mut routes = HashMap::new();
    routes.insert("/truncated".into(), Route::truncated(served));
    let server = TestServer::start(routes);
    spec.url = server.url("/truncated");

    let request = plan(temp.path(), vec![spec]);
    let result = execute_plan(request, CancellationToken::new(), None)
        .await
        .expect("execute");
    assert_eq!(result.status.as_str(), "failed");
    assert_eq!(result.failed.len(), 1);
    assert_eq!(result.failed[0].code, "SIZE_MISMATCH");
    assert!(!temp.path().join("truncated.bin").exists());
    assert_no_parts(temp.path());
}

#[tokio::test]
async fn http_404_is_reported() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"missing".to_vec();
    let mut routes = HashMap::new();
    routes.insert("/missing".into(), Route::status(404));
    let server = TestServer::start(routes);

    let result = execute_plan(
        plan(
            temp.path(),
            vec![file(server.url("/missing"), &body, "missing.bin")],
        ),
        CancellationToken::new(),
        None,
    )
    .await
    .expect("execute");

    assert_eq!(result.status.as_str(), "failed");
    assert_eq!(result.failed[0].code, "HTTP_4XX");
    assert_eq!(result.failed[0].attempts, 1);
    assert!(!temp.path().join("missing.bin").exists());
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn cancellation_stops_download_and_cleans_part() {
    let temp = TempDir::new().expect("tempdir");
    let body = vec![7_u8; 256 * 1024];
    let mut routes = HashMap::new();
    routes.insert(
        "/slow".into(),
        Route::slow(body.clone(), Duration::from_millis(25)),
    );
    let server = TestServer::start(routes);

    let cancel = CancellationToken::new();
    let task_cancel = cancel.clone();
    let canceller = tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(80)).await;
        task_cancel.cancel();
    });

    let result = execute_plan(
        plan(
            temp.path(),
            vec![file(server.url("/slow"), &body, "slow.bin")],
        ),
        cancel,
        None,
    )
    .await
    .expect("execute");
    canceller.await.expect("canceller");

    assert_eq!(result.status.as_str(), "cancelled");
    assert!(!temp.path().join("slow.bin").exists());
    assert_no_parts(temp.path());
}

#[test]
fn plan_json_round_trip_and_validation() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"json".to_vec();
    let mut request = plan(
        temp.path(),
        vec![file("https://example.invalid/a".into(), &body, "a.bin")],
    );
    request.schema_version = 1;
    let json = serde_json::to_vec(&request).expect("serialize");
    let parsed = DownloadPlan::from_json_slice(&json).expect("parse");
    assert_eq!(parsed.plan_id, "test-plan");

    request.schema_version = 2;
    let json = serde_json::to_vec(&request).expect("serialize");
    let error = DownloadPlan::from_json_slice(&json).expect_err("reject schema");
    assert_eq!(error.code().as_str(), "INVALID_PLAN");
}

#[test]
fn http_is_only_allowed_on_loopback() {
    let temp = TempDir::new().expect("tempdir");
    let body = b"url".to_vec();
    let request = plan(
        temp.path(),
        vec![file("http://example.com/a".into(), &body, "a.bin")],
    );
    let error = validate_plan(&request).expect_err("reject public http");
    assert_eq!(error.code().as_str(), "UNSUPPORTED_SCHEME");

    let request = plan(
        temp.path(),
        vec![file("http://127.0.0.1:8080/a".into(), &body, "a.bin")],
    );
    validate_plan(&request).expect("allow loopback http");
}
