import 'package:archoera_cx/widgets/common/safe_url.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('image URLs stay on the public web', () {
    expect(isSafeImageUrl('https://cdn.modrinth.com/data/x/icon.png'), isTrue);
    expect(isSafeImageUrl('http://textures.minecraft.net/texture/x'), isTrue);
    expect(isSafeImageUrl('https://127.0.0.1/x.png'), isFalse);
    expect(isSafeImageUrl('https://10.0.0.2/x.png'), isFalse);
    expect(isSafeImageUrl('https://172.16.1.1/x.png'), isFalse);
    expect(isSafeImageUrl('https://192.168.1.2/x.png'), isFalse);
    expect(isSafeImageUrl('https://169.254.169.254/latest'), isFalse);
    expect(isSafeImageUrl('https://localhost/x.png'), isFalse);
    expect(isSafeImageUrl('https://localhost./x.png'), isFalse);
    expect(isSafeImageUrl('https://printer.local/x.png'), isFalse);
    expect(isSafeImageUrl('https://[::ffff:127.0.0.1]/x.png'), isFalse);
    expect(isSafeImageUrl('file:///etc/passwd'), isFalse);
    expect(isSafeImageUrl('https://user:pass@cdn.modrinth.com/x.png'), isFalse);
    expect(isSafeImageUrl('https://[::1]/x.png'), isFalse);
  });
}
