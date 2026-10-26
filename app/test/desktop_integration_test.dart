import 'package:archoera_cx/app/desktop_integration.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Windows closes normally after dropping close interception', () async {
    final calls = <String>[];
    await closeMainWindowForQuit(
      isWindows: true,
      setPreventClose: (prevent) async {
        calls.add('preventClose:$prevent');
      },
      close: () async {
        calls.add('close');
      },
      destroy: () async {
        calls.add('destroy');
      },
    );

    // The close path is what lets the Windows runner destroy the Flutter
    // controller from WM_DESTROY instead of from its destructor.
    expect(calls, ['preventClose:false', 'close']);
  });

  test('other platforms keep destroy', () async {
    final calls = <String>[];
    await closeMainWindowForQuit(
      isWindows: false,
      setPreventClose: (prevent) async {
        calls.add('preventClose:$prevent');
      },
      close: () async {
        calls.add('close');
      },
      destroy: () async {
        calls.add('destroy');
      },
    );

    expect(calls, ['destroy']);
  });
}
