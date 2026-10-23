/*
 * Display adapter fallback.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * macOS switches GPUs itself and has no per-process override, so reporting an
 * empty list is the honest answer: the launch module then adds no GPU flags.
 */

#include "platform_gpu.h"

#include "platform_status.h"

namespace archoera {

const GpuStatus& sample_gpu_status() {
  static const GpuStatus status = [] {
    GpuStatus result;
    result.platform = platform_name();
    return result;
  }();
  return status;
}

}  // namespace archoera
