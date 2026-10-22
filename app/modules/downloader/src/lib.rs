#![forbid(unsafe_code)]

//! `archoera_dl` — a standalone DownloadPlan executor.
//!
//! This crate turns a C#-produced [`DownloadPlan`] into files. It deliberately
//! does **not** generate URLs, understand Mojang/Modrinth/loader semantics,
//! extract archives, or write SQLite. The C++ ModuleHost can load the `cdylib`
//! later; this build intentionally exports no ABI symbols yet.
//!
//! # Example
//!
//! ```no_run
//! use archoera_dl::{DownloadPlan, CancellationToken, execute_plan};
//!
//! # async fn run(json: &str) -> Result<(), Box<dyn std::error::Error>> {
//! let plan = DownloadPlan::from_json_slice(json.as_bytes())?;
//! let result = execute_plan(plan, CancellationToken::new(), None).await?;
//! println!("{}", result.to_json()?);
//! # Ok(())
//! # }
//! ```

mod edge;
mod error;
mod executor;
mod hash;
mod limiter;
mod plan;
mod progress;
mod result;

pub use error::{DownloadError, ErrorCode};
pub use plan::{
    DEFAULT_CONCURRENCY, DownloadPlan, Durability, FileMode, FileSpec, HashSpec, MAX_CONCURRENCY,
    MAX_FILE_BYTES, MAX_FILES, MAX_PLAN_JSON_BYTES, PlanOptions, SCHEMA_VERSION, VerifyExisting,
    validate_plan,
};
pub use progress::{Progress, ProgressCallback};
pub use result::{DownloadResult, FailedFile, Status};
pub use tokio_util::sync::CancellationToken;

/// Validate, then execute a plan.
///
/// Validation failures are returned as [`DownloadError`] and no files are
/// touched. Per-file failures are reported inside [`DownloadResult::failed`];
/// the function still returns `Ok` unless the plan itself is invalid or the
/// client cannot be constructed.
///
/// The callback is invoked from download tasks (throttled to
/// `options.progressIntervalMs`, default 150 ms) and once at completion.
pub async fn execute_plan(
    plan: DownloadPlan,
    cancel: CancellationToken,
    progress: Option<ProgressCallback>,
) -> Result<DownloadResult, DownloadError> {
    executor::execute(plan, cancel, progress).await
}
