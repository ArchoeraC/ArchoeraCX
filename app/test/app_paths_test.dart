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

  group('instanceFolderName', () {
    test('keeps unicode, case and ordinary symbols', () {
      expect(instanceFolderName('  My Instance!  '), 'My Instance!');
      expect(instanceFolderName('1.21.4 · Fabric'), '1.21.4 · Fabric');
      expect(instanceFolderName('生存整合包'), '生存整合包');
      expect(instanceFolderName('a_b-c.d'), 'a_b-c.d');
      expect(instanceFolderName('a[1] {x} (y)'), 'a[1] {x} (y)');
    });

    test('turns separators into dashes', () {
      expect(instanceFolderName('A/B:C'), 'A-B-C');
      expect(instanceFolderName(r'A\B'), 'A-B');
    });

    test('falls back for empty or dot-only names', () {
      expect(instanceFolderName(''), 'instance');
      expect(instanceFolderName('   '), 'instance');
      expect(instanceFolderName('.'), 'instance');
      expect(instanceFolderName('..'), 'instance');
      expect(instanceFolderName('...   '), 'instance');
      expect(instanceFolderName(' . . '), 'instance');
    });

    test('keeps interior dots', () {
      expect(instanceFolderName('foo..bar'), 'foo..bar');
    });

    test('avoids windows device names', () {
      expect(instanceFolderName('CON'), '_CON');
      expect(instanceFolderName('com1.txt'), '_com1.txt');
    });
  });

  group('instanceNameProblem', () {
    test('accepts unicode and ordinary symbols', () {
      expect(instanceNameProblem('My Instance!'), isNull);
      expect(instanceNameProblem('1.21.4 · Fabric'), isNull);
      expect(instanceNameProblem('生存整合包'), isNull);
      expect(instanceNameProblem('a[1] {x} (y)'), isNull);
    });

    test('reports filesystem and windows problems', () {
      expect(instanceNameProblem(''), InstanceNameProblem.empty);
      expect(instanceNameProblem('   '), InstanceNameProblem.empty);
      expect(
        instanceNameProblem('a'.padRight(instanceNameMaxLength + 1, 'a')),
        InstanceNameProblem.tooLong,
      );
      expect(instanceNameProblem('A/B'), InstanceNameProblem.invalidCharacters);
      expect(instanceNameProblem('A:B'), InstanceNameProblem.invalidCharacters);
      expect(instanceNameProblem('.hidden'), InstanceNameProblem.invalidEdges);
      expect(instanceNameProblem('name.'), InstanceNameProblem.invalidEdges);
      expect(instanceNameProblem('CON'), InstanceNameProblem.reservedName);
      expect(instanceNameProblem('com1.txt'), InstanceNameProblem.reservedName);
    });
  });
}
