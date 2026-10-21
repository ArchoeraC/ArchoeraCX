//! Fixed-shape result JSON (docs/10 §9.8).

use serde::Serialize;

use crate::error::DownloadError;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Status {
    Completed,
    Partial,
    Failed,
    Cancelled,
}

impl Status {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Completed => "completed",
            Self::Partial => "partial",
            Self::Failed => "failed",
            Self::Cancelled => "cancelled",
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FailedFile {
    pub dest: String,
    pub code: String,
    pub source: Option<String>,
    pub attempts: u32,
    pub retryable: bool,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DownloadResult {
    pub plan_id: String,
    pub status: Status,
    pub ok: u64,
    pub skipped: u64,
    pub failed: Vec<FailedFile>,
    pub bytes: u64,
    pub elapsed_ms: u64,
    /// Compatibility field: v1 never executes post actions.
    pub post: Vec<serde_json::Value>,
}

impl DownloadResult {
    pub fn to_json(&self) -> Result<String, DownloadError> {
        serde_json::to_string(self)
            .map_err(|err| DownloadError::Internal(format!("result serialization failed: {err}")))
    }
}
