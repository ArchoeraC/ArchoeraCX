import 'package:archoera_cx/utils/app_paths.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('resolveDefaultDataRoot', () {
    test('explicit environment override wins', () {
      expect(
        resolveDefaultDataRoot(
          {'ARCHOERACX_DATA_DIR': ' /portable/archoera '},
          isWindows: true,
          isMacOs: true,
          systemTempPath: '/tmp',
        ),
        '/portable/archoera',
      );
    });

    test('Windows uses APPDATA', () {
      expect(
        resolveDefaultDataRoot(
          {r'APPDATA': r'C:\Users\u\AppData\Roaming'},
          isWindows: true,
          isMacOs: false,
          systemTempPath: r'C:\Temp',
        ),
        joinPath(r'C:\Users\u\AppData\Roaming', 'ArchoeraCX', separator: '\\'),
      );
    });

    test('macOS uses Application Support', () {
      expect(
        resolveDefaultDataRoot(
          {'HOME': '/Users/example'},
          isWindows: false,
          isMacOs: true,
          systemTempPath: '/tmp',
        ),
        '/Users/example/Library/Application Support/ArchoeraCX',
      );
    });

    test('Linux uses absolute XDG_DATA_HOME', () {
      expect(
        resolveDefaultDataRoot(
          {'XDG_DATA_HOME': '/xdg/data', 'HOME': '/home/u'},
          isWindows: false,
          isMacOs: false,
          systemTempPath: '/tmp',
        ),
        '/xdg/data/archoera-cx',
      );
    });

    test('Linux ignores a relative XDG_DATA_HOME', () {
      expect(
        resolveDefaultDataRoot(
          {'XDG_DATA_HOME': 'relative/data', 'HOME': '/home/u'},
          isWindows: false,
          isMacOs: false,
          systemTempPath: '/tmp',
        ),
        '/home/u/.local/share/archoera-cx',
      );
    });

    test('missing HOME falls back to the OS temp directory', () {
      expect(
        resolveDefaultDataRoot(
          const {},
          isWindows: false,
          isMacOs: false,
          systemTempPath: '/var/tmp',
        ),
        '/var/tmp/archoera-cx',
      );
    });
  });

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
