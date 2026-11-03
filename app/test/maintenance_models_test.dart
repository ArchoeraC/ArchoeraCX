import 'package:archoera_cx/services/core/downloads/maintenance.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('unused dependency plan parses the native envelope', () {
    final plan = UnusedDependencyPlan.fromNative(<String, Object?>{
      'totalFiles': 3,
      'totalBytes': 2400,
      'limitReached': false,
      'roots': <Object?>[
        <String, Object?>{
          'root': '/minecraft',
          'status': 'scanned',
          'instanceCount': 4,
          'libraries': <String, Object?>{
            'files': 2,
            'bytes': 1600,
            'samples': <String>['libraries/a.jar'],
          },
          'assets': <String, Object?>{
            'files': 1,
            'bytes': 800,
            'samples': <String>['assets/objects/aa/hash'],
          },
        },
        <String, Object?>{
          'root': '/empty',
          'status': 'skipped',
          'reason': 'no_instances',
        },
      ],
    });

    expect(plan.totalFiles, 3);
    expect(plan.totalBytes, 2400);
    expect(plan.limitReached, isFalse);
    expect(plan.roots, hasLength(2));

    final scanned = plan.roots[0];
    expect(scanned.skipped, isFalse);
    expect(scanned.instanceCount, 4);
    expect(scanned.libraries!.kind, 'libraries');
    expect(scanned.libraries!.files, 2);
    expect(scanned.libraries!.samples, ['libraries/a.jar']);
    expect(scanned.assets!.files, 1);

    final skipped = plan.roots[1];
    expect(skipped.skipped, isTrue);
    expect(skipped.reason, 'no_instances');
    expect(skipped.libraries, isNull);
  });

  test('a failed asset index keeps the libraries group parseable', () {
    final plan = UnusedDependencyPlan.fromNative(<String, Object?>{
      'totalFiles': 1,
      'totalBytes': 8,
      'limitReached': false,
      'roots': <Object?>[
        <String, Object?>{
          'root': '/minecraft',
          'status': 'scanned',
          'instanceCount': 1,
          'libraries': <String, Object?>{'files': 1, 'bytes': 8, 'samples': <String>[]},
        },
      ],
    });

    expect(plan.roots.single.libraries, isNotNull);
    expect(plan.roots.single.assets, isNull);
  });

  test('a malformed payload fails closed as an empty limited plan', () {
    final plan = UnusedDependencyPlan.fromNative('not a map');
    expect(plan.totalFiles, 0);
    expect(plan.limitReached, isTrue);
    expect(plan.roots, isEmpty);
  });
}
