import 'package:archoera_cx/app/licenses.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('bundled icon font license is registered for the license page', () async {
    registerAssetLicenses();

    final entry = await LicenseRegistry.licenses
        .firstWhere(
          (entry) => entry.packages.any(
            (package) => package.startsWith('AppIcons'),
          ),
        )
        .timeout(const Duration(seconds: 5));

    expect(entry.packages.join(), contains('Tabler Icons'));
    final text = entry.paragraphs.map((paragraph) => paragraph.text).join('\n');
    expect(text, contains('MIT License'));
    expect(text, contains('Paweł Kuna'));
  });
}
