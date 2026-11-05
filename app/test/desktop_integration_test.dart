import 'package:archoera_cx/app/desktop_integration.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('closing the window uses the platform close path', () async {
    var closed = false;
    final exits = <int>[];

    await closeMainWindowForQuit(
      closeWindow: () {
        closed = true;
        return true;
      },
      quitProcess: exits.add,
    );

    // Approving the close request is what lets every desktop runner quit with
    // its last window, including the Windows runner's WM_DESTROY path.
    expect(closed, isTrue);
    expect(exits, isEmpty);
  });

  test('ends the process when the host cannot close a window', () async {
    final exits = <int>[];

    await closeMainWindowForQuit(
      closeWindow: () => false,
      quitProcess: exits.add,
    );

    expect(exits, [0]);
  });
}
