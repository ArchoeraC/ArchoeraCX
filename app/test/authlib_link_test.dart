import 'package:archoera_cx/utils/authlib_link.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('reads the authlib-injector link format', () {
    expect(
      parseAuthlibServerLink(
        'authlib-injector:yggdrasil-server:'
        'https%3A%2F%2Flittleskin.cn%2Fapi%2Fyggdrasil',
      ),
      'https://littleskin.cn/api/yggdrasil',
    );
    expect(
      parseAuthlibServerLink('https://skin.prinzeugen.net/api/yggdrasil'),
      'https://skin.prinzeugen.net/api/yggdrasil',
    );
    expect(parseAuthlibServerLink('not a link'), isNull);
    expect(
      parseAuthlibServerLink('authlib-injector:yggdrasil-server:%2F%2Fevil'),
      isNull,
    );
  });
}
