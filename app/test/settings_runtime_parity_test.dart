import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/settings/app_settings.dart';
import 'package:archoera_cx/utils/window_mode.dart';
import 'package:flutter_test/flutter_test.dart';

AppSettings _settings(Map<String, Object?> extra) => AppSettings.tryParse(
  <String, Object?>{'instancesRoot': '/games', 'dataRoot': '/data', ...extra},
)!;

void main() {
  group('AppSettings runtime parity fields', () {
    test('defaults keep every new switch off and no native paths', () {
      final settings = _settings(const <String, Object?>{});

      expect(settings.maximized, isFalse);
      expect(settings.fullscreen, isFalse);
      expect(settings.enableGameMode, isFalse);
      expect(settings.enableMangoHud, isFalse);
      expect(settings.useZink, isFalse);
      expect(settings.developerMode, isFalse);
      expect(settings.nativeGlfwPath, isNull);
      expect(settings.nativeOpenALPath, isNull);
      expect(settings.nativeSdlPath, isNull);
    });

    test('parses performance, developer and native library settings', () {
      final settings = _settings(<String, Object?>{
        'maximized': true,
        'enableGameMode': true,
        'enableMangoHud': true,
        'useZink': true,
        'developerMode': true,
        'nativeGlfwPath': '  /usr/lib/libglfw.so.3  ',
        'nativeOpenALPath': '/usr/lib/libopenal.so.1',
        'nativeSdlPath': r'C:\Games\SDL2.dll',
      });

      expect(settings.maximized, isTrue);
      expect(settings.enableGameMode, isTrue);
      expect(settings.enableMangoHud, isTrue);
      expect(settings.useZink, isTrue);
      expect(settings.developerMode, isTrue);
      expect(settings.nativeGlfwPath, '/usr/lib/libglfw.so.3');
      expect(settings.nativeOpenALPath, '/usr/lib/libopenal.so.1');
      expect(settings.nativeSdlPath, r'C:\Games\SDL2.dll');
    });

    test('fullscreen wins when a malformed document sets both flags', () {
      final settings = _settings(<String, Object?>{
        'fullscreen': true,
        'maximized': true,
      });

      expect(settings.fullscreen, isTrue);
      expect(settings.maximized, isFalse);
      expect(
        gameWindowModeFrom(
          fullscreen: settings.fullscreen,
          maximized: settings.maximized,
        ),
        GameWindowMode.fullscreen,
      );
    });

    test('rejects relative, blank and non-string native paths', () {
      final settings = _settings(<String, Object?>{
        'nativeGlfwPath': 'libglfw.so',
        'nativeOpenALPath': '   ',
        'nativeSdlPath': 42,
      });

      expect(settings.nativeGlfwPath, isNull);
      expect(settings.nativeOpenALPath, isNull);
      expect(settings.nativeSdlPath, isNull);
    });

    test('ignores non-boolean switches', () {
      final settings = _settings(<String, Object?>{
        'maximized': 'true',
        'enableGameMode': 1,
        'enableMangoHud': 'yes',
        'useZink': <String>['1'],
        'developerMode': null,
      });

      expect(settings.maximized, isFalse);
      expect(settings.enableGameMode, isFalse);
      expect(settings.enableMangoHud, isFalse);
      expect(settings.useZink, isFalse);
      expect(settings.developerMode, isFalse);
    });
  });

  group('InstanceOverrides runtime parity fields', () {
    test('absent fields inherit the global values', () {
      final overrides = InstanceOverrides.tryParse(<String, Object?>{
        'memoryMaxMb': 4096,
      })!;

      expect(overrides.maximized, isNull);
      expect(overrides.enableGameMode, isNull);
      expect(overrides.enableMangoHud, isNull);
      expect(overrides.useZink, isNull);
    });

    test('parses explicit on/off performance overrides', () {
      final overrides = InstanceOverrides.tryParse(<String, Object?>{
        'maximized': true,
        'enableGameMode': true,
        'enableMangoHud': false,
        'useZink': true,
      })!;

      expect(overrides.maximized, isTrue);
      expect(overrides.fullscreen, isNull);
      expect(overrides.enableGameMode, isTrue);
      expect(overrides.enableMangoHud, isFalse);
      expect(overrides.useZink, isTrue);
      expect(overrides.isEmpty, isFalse);
    });

    test('a performance override alone is not treated as empty', () {
      final overrides = InstanceOverrides.tryParse(<String, Object?>{
        'enableMangoHud': false,
      })!;

      expect(overrides.isEmpty, isFalse);
      expect(overrides.copyWith(useZink: true).useZink, isTrue);
      // copyWith keeps the values that were not passed.
      expect(overrides.copyWith(useZink: true).enableMangoHud, isFalse);
    });

    test('fullscreen wins over a conflicting maximized value', () {
      final overrides = InstanceOverrides.tryParse(<String, Object?>{
        'fullscreen': true,
        'maximized': true,
      })!;

      expect(overrides.fullscreen, isTrue);
      expect(overrides.maximized, isNull);
      expect(
        instanceWindowModeFrom(
          fullscreen: overrides.fullscreen,
          maximized: overrides.maximized,
        ),
        GameWindowMode.fullscreen,
      );
    });
  });

  group('window mode mapping', () {
    test('global flags map to one mode', () {
      expect(
        gameWindowModeFrom(fullscreen: false, maximized: false),
        GameWindowMode.windowed,
      );
      expect(
        gameWindowModeFrom(fullscreen: true, maximized: false),
        GameWindowMode.fullscreen,
      );
      expect(
        gameWindowModeFrom(fullscreen: false, maximized: true),
        GameWindowMode.maximized,
      );
    });

    test('instance flags keep the inherit state', () {
      expect(instanceWindowModeFrom(fullscreen: null, maximized: null), isNull);
      expect(
        instanceWindowModeFrom(fullscreen: false, maximized: null),
        GameWindowMode.windowed,
      );
      expect(
        instanceWindowModeFrom(fullscreen: true, maximized: null),
        GameWindowMode.fullscreen,
      );
      expect(
        instanceWindowModeFrom(fullscreen: false, maximized: true),
        GameWindowMode.maximized,
      );
    });
  });
}
