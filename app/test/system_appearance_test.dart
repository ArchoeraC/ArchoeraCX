import 'package:archoera_cx/services/core/system_appearance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('a late empty reply does not wipe a known accent', () {
    const event = SystemAppearance(
      theme: 'dark',
      accent: Color(0xFF61936D),
      source: 'kde-config',
    );
    const lateEmpty = SystemAppearance(
      theme: 'unknown',
      accent: null,
      source: 'unknown',
    );

    final merged = lateEmpty.merge(event);

    expect(merged.theme, 'dark');
    expect(merged.accent, const Color(0xFF61936D));
    expect(merged.source, 'kde-config');
  });

  test('newer known fields win', () {
    const older = SystemAppearance(
      theme: 'light',
      accent: Color(0xFF3584E4),
      source: 'xdg-portal',
    );
    const newer = SystemAppearance(
      theme: 'dark',
      accent: Color(0xFF61936D),
      source: 'kde-config',
    );

    final merged = newer.merge(older);

    expect(merged.theme, 'dark');
    expect(merged.accent, const Color(0xFF61936D));
    expect(merged.source, 'kde-config');
  });

  test('merge without a previous snapshot returns the input', () {
    const only = SystemAppearance(
      theme: 'unknown',
      accent: Color(0xFF61936D),
      source: 'kde-config',
    );

    expect(only.merge(null), same(only));
  });
}
