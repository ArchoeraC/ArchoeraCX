import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/models/launch_options.dart';
import 'package:archoera_cx/services/core/settings/app_settings.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/settings.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

AppSettings _settings(Map<String, Object?> extra) => AppSettings.tryParse(
  <String, Object?>{'instancesRoot': '/games', 'dataRoot': '/data', ...extra},
)!;

InstanceOverrides _overrides(Map<String, Object?> value) =>
    InstanceOverrides.tryParse(value)!;

void main() {
  group('AppSettings launch process fields', () {
    test('defaults to no wrapper and an empty environment', () {
      final settings = _settings(const <String, Object?>{});

      expect(settings.wrapperCommand, isNull);
      expect(settings.environment, isEmpty);
    });

    test('parses a wrapper and environment map', () {
      final settings = _settings(<String, Object?>{
        'wrapperCommand': '  gamemoderun  ',
        'environment': <String, Object?>{
          'MESA_LOADER_DRIVER_OVERRIDE': 'zink',
          'DRI_PRIME': '1',
        },
      });

      expect(settings.wrapperCommand, 'gamemoderun');
      expect(settings.environment, <String, String>{
        'MESA_LOADER_DRIVER_OVERRIDE': 'zink',
        'DRI_PRIME': '1',
      });
    });

    test('collapses an empty global wrapper and skips bad map entries', () {
      final settings = _settings(<String, Object?>{
        'wrapperCommand': '   ',
        'environment': <Object?, Object?>{
          'KEEP': 'yes',
          1: 'not-a-string-key',
          'DROP': 2,
        },
      });

      expect(settings.wrapperCommand, isNull);
      expect(settings.environment, <String, String>{'KEEP': 'yes'});
    });

    test('a malformed environment value is treated as absent', () {
      final settings = _settings(<String, Object?>{'environment': 'KEY=VALUE'});

      expect(settings.environment, isEmpty);
    });
  });

  group('InstanceOverrides launch process fields', () {
    test('absent wrapper/environment/auto-join inherit the global values', () {
      final overrides = InstanceOverrides.tryParse(<String, Object?>{
        'memoryMaxMb': 4096,
      })!;

      expect(overrides.wrapperCommand, isNull);
      expect(overrides.environment, isNull);
      expect(overrides.joinServerOnLaunch, isNull);
      expect(overrides.isEmpty, isFalse);
    });

    test('an empty wrapper and empty environment are explicit overrides', () {
      final overrides = _overrides(<String, Object?>{
        'wrapperCommand': '',
        'environment': <String, Object?>{},
      });

      expect(overrides.wrapperCommand, '');
      expect(overrides.environment, isEmpty);
      expect(overrides.environment, isNotNull);
      expect(overrides.isEmpty, isFalse);
    });

    test('parses auto-join fields and defensive values', () {
      final overrides = _overrides(<String, Object?>{
        'wrapperCommand': '  prime-run  ',
        'environment': <String, Object?>{'DRI_PRIME': '1'},
        'joinServerOnLaunch': true,
        'joinServerAddress': '  example.com:25565  ',
        'joinWorldName': '  New World  ',
      });

      expect(overrides.wrapperCommand, 'prime-run');
      expect(overrides.environment, <String, String>{'DRI_PRIME': '1'});
      expect(overrides.joinServerOnLaunch, isTrue);
      expect(overrides.joinServerAddress, 'example.com:25565');
      expect(overrides.joinWorldName, 'New World');

      final cleared = _overrides(<String, Object?>{
        'joinServerOnLaunch': false,
        'environment': null,
      });
      expect(cleared.joinServerOnLaunch, isFalse);
      expect(cleared.environment, isNull);
    });

    test('copyWith carries every new field', () {
      const original = InstanceOverrides();
      final copy = original.copyWith(
        wrapperCommand: '',
        environment: const <String, String>{'A': 'b'},
        joinServerOnLaunch: true,
        joinServerAddress: 'localhost',
        joinWorldName: 'world',
      );

      expect(copy.wrapperCommand, '');
      expect(copy.environment, const <String, String>{'A': 'b'});
      expect(copy.joinServerOnLaunch, isTrue);
      expect(copy.joinServerAddress, 'localhost');
      expect(copy.joinWorldName, 'world');
      expect(copy.isEmpty, isFalse);
    });
  });

  group('environment block parser', () {
    test('parses blank lines, CRLF and values containing equals', () {
      final result = parseLaunchEnvironment('A=1\r\n\r\nB=two=three\nC=\n');

      expect(result.isValid, isTrue);
      expect(result.values, <String, String>{
        'A': '1',
        'B': 'two=three',
        'C': '',
      });
      expect(formatLaunchEnvironment(result.values), 'A=1\nB=two=three\nC=');
    });

    test('reports a missing equals sign with its line number', () {
      final result = parseLaunchEnvironment('A=1\nNOT_A_PAIR');

      expect(result.isValid, isFalse);
      expect(result.issue?.kind, LaunchEnvironmentIssueKind.missingSeparator);
      expect(result.issue?.line, 2);
    });

    test('reports invalid, duplicate and reserved keys', () {
      expect(
        parseLaunchEnvironment('BAD KEY=1').issue?.kind,
        LaunchEnvironmentIssueKind.invalidKey,
      );
      expect(
        parseLaunchEnvironment('A=1\nA=2').issue?.kind,
        LaunchEnvironmentIssueKind.duplicateKey,
      );
      expect(
        parseLaunchEnvironment('JAVA_TOOL_OPTIONS=-Xmx1g').issue?.kind,
        LaunchEnvironmentIssueKind.reservedKey,
      );
      expect(
        parseLaunchEnvironment('_JAVA_OPTIONS=-Xmx1g').issue?.kind,
        LaunchEnvironmentIssueKind.reservedKey,
      );
    });

    test('limits entries and value length', () {
      final tooMany = List.generate(
        65,
        (index) => 'KEY_$index=value',
      ).join('\n');
      expect(
        parseLaunchEnvironment(tooMany).issue?.kind,
        LaunchEnvironmentIssueKind.tooManyEntries,
      );

      final longValue = 'KEY=${'x' * 4097}';
      expect(
        parseLaunchEnvironment(longValue).issue?.kind,
        LaunchEnvironmentIssueKind.invalidValue,
      );
      expect(
        parseLaunchEnvironment('KEY=a\u0000b').issue?.kind,
        LaunchEnvironmentIssueKind.invalidValue,
      );
    });
  });

  test('the fallback settings store accepts an empty map literal', () async {
    final container = ProviderContainer(
      overrides: [coreClientProvider.overrideWithValue(null)],
    );
    addTearDown(container.dispose);
    await container.read(appSettingsProvider.future);

    await container
        .read(appSettingsProvider.notifier)
        .setRuntimeOptions(environment: {}, wrapperCommand: 'mangohud');

    final settings = container.read(appSettingsProvider).value;
    expect(settings?.wrapperCommand, 'mangohud');
    expect(settings?.environment, isEmpty);
  });

  group('launch value validation', () {
    test('wrapper commands respect the length and NUL limits', () {
      expect(isValidLaunchWrapperCommand(''), isTrue);
      expect(isValidLaunchWrapperCommand('gamemoderun'), isTrue);
      expect(
        isValidLaunchWrapperCommand('x' * launchWrapperCommandMaxLength),
        isTrue,
      );
      expect(
        isValidLaunchWrapperCommand('x' * (launchWrapperCommandMaxLength + 1)),
        isFalse,
      );
      expect(isValidLaunchWrapperCommand('run\u0000game'), isFalse);
      expect(isValidLaunchWrapperCommand('run\nmangohud'), isFalse);
      expect(isValidLaunchWrapperCommand('run\rmangohud'), isFalse);
    });

    test('accepts host, host:port and bracketed IPv6 forms', () {
      for (final address in <String>[
        'example.com',
        'localhost:25565',
        'mc.example.net:1',
        '[::1]',
        '[2001:db8::1]:25565',
        '[::ffff:192.0.2.1]:65535',
      ]) {
        expect(isValidJoinServerAddress(address), isTrue, reason: address);
      }
    });

    test('rejects malformed addresses and out-of-range ports', () {
      for (final address in <String>[
        '',
        'example.com:',
        ':25565',
        'example.com:0',
        'example.com:65536',
        'example.com:port',
        '[::1',
        '[not:v6]',
        '1:2:3',
        'host name',
        'host/path',
        ' example.com',
        'example.com ',
        '${'a' * 256}.example',
      ]) {
        expect(isValidJoinServerAddress(address), isFalse, reason: address);
      }
    });

    test('validates singleplayer world names', () {
      expect(isValidJoinWorldName('New World'), isTrue);
      expect(isValidJoinWorldName('x' * 128), isTrue);
      expect(isValidJoinWorldName(''), isFalse);
      expect(isValidJoinWorldName('x' * 129), isFalse);
      expect(isValidJoinWorldName('saves/world'), isFalse);
      expect(isValidJoinWorldName(r'saves\world'), isFalse);
      expect(isValidJoinWorldName('../world'), isFalse);
      expect(isValidJoinWorldName('world\u0001'), isFalse);
      expect(isValidJoinWorldName('world\u0085'), isFalse);
    });
  });
}
