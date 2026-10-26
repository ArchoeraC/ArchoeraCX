import 'dart:async';

import 'package:archoera_cx/app/single_instance.dart';
import 'package:archoera_cx/services/core/core_event.dart';
import 'package:flutter_test/flutter_test.dart';

CoreEvent _event({
  CoreEventType type = CoreEventType.domain,
  Map<String, Object?> payload = const <String, Object?>{},
}) {
  return CoreEvent(seq: 1, type: type, taskId: 0, payload: payload);
}

void main() {
  test('recognises the platform activation domain event', () {
    expect(
      isSingleInstanceActivation(
        _event(
          payload: const <String, Object?>{
            'kind': singleInstanceActivationKind,
          },
        ),
      ),
      isTrue,
    );
  });

  test('ignores other domain and task events', () {
    expect(
      isSingleInstanceActivation(
        _event(payload: const <String, Object?>{'kind': 'system.status'}),
      ),
      isFalse,
    );
    expect(
      isSingleInstanceActivation(
        _event(
          type: CoreEventType.taskState,
          payload: const <String, Object?>{
            'kind': singleInstanceActivationKind,
          },
        ),
      ),
      isFalse,
    );
  });

  test('wakes the existing window once per activation message', () async {
    final controller = StreamController<CoreEvent>();
    var activations = 0;
    final subscription = listenForSingleInstanceActivation(
      controller.stream,
      () async {
        activations++;
      },
    );

    controller.add(_event(payload: const {'kind': 'system.status'}));
    controller.add(
      _event(payload: const {'kind': singleInstanceActivationKind}),
    );
    await Future<void>.delayed(Duration.zero);

    expect(activations, 1);
    await subscription.cancel();
    await controller.close();
  });
}
