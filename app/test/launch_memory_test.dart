import 'package:archoera_cx/services/core/settings/app_settings.dart';
import 'package:archoera_cx/services/core/launch/launch_memory.dart';
import 'package:flutter_test/flutter_test.dart';

/// The native module owns heap sizing; the UI only parses the preview and maps
/// the warning code to localized text.
void main() {
  test('parses the native memory preview', () {
    final plan = LaunchMemoryPlan.tryParse(<String, Object?>{
      'mode': 'manual',
      'minMb': 1024,
      'maxMb': 512,
      'recommendedMb': 4096,
      'systemTotalMb': 32768,
      'systemAvailableMb': 16384,
      'modCount': 126,
      'warningCode': 'LOW_MEMORY',
      'warning': 'heap 512 MB is below 1024 MB',
    });

    expect(plan, isNotNull);
    expect(plan!.mode, 'manual');
    expect(plan.isAuto, isFalse);
    expect(plan.minMb, 1024);
    expect(plan.maxMb, 512);
    expect(plan.recommendedMb, 4096);
    expect(plan.systemTotalMb, 32768);
    expect(plan.systemAvailableMb, 16384);
    expect(plan.modCount, 126);
    expect(plan.hasWarning, isTrue);
    expect(plan.isBelowRecommended, isTrue);
  });

  test('an automatic plan without a warning is clean', () {
    final plan = LaunchMemoryPlan.tryParse(<String, Object?>{
      'mode': 'auto',
      'minMb': 1024,
      'maxMb': 4096,
      'recommendedMb': 4096,
      'systemTotalMb': 32768,
      'systemAvailableMb': null,
      'modCount': 0,
      'warningCode': null,
      'warning': null,
    });

    expect(plan, isNotNull);
    expect(plan!.isAuto, isTrue);
    expect(plan.hasWarning, isFalse);
    expect(plan.isBelowRecommended, isFalse);
    expect(plan.systemAvailableMb, isNull);
  });

  test('a payload without the heap numbers is rejected', () {
    expect(LaunchMemoryPlan.tryParse(<String, Object?>{}), isNull);
    expect(LaunchMemoryPlan.tryParse('auto'), isNull);
    expect(LaunchMemoryPlan.tryParse(<String, Object?>{'maxMb': 2048}), isNull);
  });

  test('settings default to automatic memory with optimization on', () {
    final settings = AppSettings.tryParse(<String, Object?>{
      'instancesRoot': '/games',
      'dataRoot': '/data',
    });

    expect(settings, isNotNull);
    expect(settings!.memoryMode, 'auto');
    expect(settings.memoryAuto, isTrue);
    expect(settings.gameOptimization, isTrue);
  });

  test('settings parse a manual heap with optimization off', () {
    final settings = AppSettings.tryParse(<String, Object?>{
      'instancesRoot': '/games',
      'dataRoot': '/data',
      'memoryMode': 'manual',
      'gameOptimization': false,
    });

    expect(settings, isNotNull);
    expect(settings!.memoryAuto, isFalse);
    expect(settings.gameOptimization, isFalse);
  });

  test('an unknown memory mode falls back to automatic', () {
    final settings = AppSettings.tryParse(<String, Object?>{
      'instancesRoot': '/games',
      'dataRoot': '/data',
      'memoryMode': 'sometimes',
    });

    expect(settings, isNotNull);
    expect(settings!.memoryAuto, isTrue);
  });
}
