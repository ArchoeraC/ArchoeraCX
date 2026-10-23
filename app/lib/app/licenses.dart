import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// Bundled assets whose licenses Flutter cannot discover on its own.
///
/// Flutter collects `LICENSE` files from Dart/pub dependencies automatically,
/// but fonts and images shipped as plain assets are invisible to that scan.
/// Registering them here makes `showLicensePage` list them next to the
/// package licenses.
const _assetLicenses = <(String, String)>[
  (
    'AppIcons — Tabler Icons 3.49.0 (outline glyph subset)',
    'assets/licenses/Tabler-Icons-MIT.txt',
  ),
];

/// Call once during startup, before the first `showLicensePage`.
void registerAssetLicenses() {
  LicenseRegistry.addLicense(() async* {
    for (final (title, asset) in _assetLicenses) {
      final text = await rootBundle.loadString(asset);
      yield LicenseEntryWithLineBreaks([title], text);
    }
  });
}
