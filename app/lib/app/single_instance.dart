import 'dart:async';

import '../services/core/core_event.dart';

/// Domain event emitted by the primary process when another launch forwards an
/// activation message.
const singleInstanceActivationKind = 'platform.activate';

/// True when [event] asks the existing launcher window to come forward.
bool isSingleInstanceActivation(CoreEvent event) {
  return event.type == CoreEventType.domain &&
      event.payload['kind'] == singleInstanceActivationKind;
}

/// Subscribes to native activations until the returned subscription is
/// cancelled. The callback runs on the UI isolate, driven by the core wake
/// channel; this never polls.
StreamSubscription<CoreEvent> listenForSingleInstanceActivation(
  Stream<CoreEvent> events,
  Future<void> Function() onActivate,
) {
  return events.where(isSingleInstanceActivation).listen((_) {
    unawaited(onActivate());
  });
}
