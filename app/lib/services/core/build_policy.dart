import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Compile-time distribution policy.
///
/// Mainland-China distribution builds are compiled with
/// `--dart-define=ARCHOERACX_CHINA_BUILD=true`; the native core and C# module
/// are built with the matching `ARCHOERACX_CHINA_BUILD`/`ChinaBuild` flag.
const bool kChinaBuild = bool.fromEnvironment(
  'ARCHOERACX_CHINA_BUILD',
  defaultValue: false,
);

/// UI-level availability check. Tests can override this provider without
/// changing the compile-time native policy.
final downloadEnabledProvider = Provider<bool>((ref) => !kChinaBuild);
