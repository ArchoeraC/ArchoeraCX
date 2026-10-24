import 'package:archoera_cx/utils/app_paths.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('slugForInstance', () {
    test('normalizes a display name', () {
      expect(slugForInstance('  My Instance!  '), 'my-instance');
      expect(slugForInstance('1.21.4 NeoForge'), '1.21.4-neoforge');
      expect(slugForInstance('a_b-c.d'), 'a_b-c.d');
    });

    test('falls back for empty or separator-only names', () {
      expect(slugForInstance(''), 'instance');
      expect(slugForInstance('   '), 'instance');
      expect(slugForInstance('---'), 'instance');
    });

    test('rejects dot-only slugs that would escape the instances root', () {
      expect(slugForInstance('.'), 'instance');
      expect(slugForInstance('..'), 'instance');
      expect(slugForInstance('...'), 'instance');
      expect(slugForInstance('...   '), 'instance');
    });

    test('keeps interior dots', () {
      expect(slugForInstance('foo..bar'), 'foo..bar');
      expect(slugForInstance(' . . '), '.-.');
    });
  });
}
