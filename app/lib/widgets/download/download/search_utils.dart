part of '../resource_search_panel.dart';

IconData _kindIcon(String kind) => switch (kind) {
  'shader' => AppIcons.shader,
  'resourcepack' => AppIcons.resourcePack,
  'modpack' => AppIcons.modpack,
  _ => AppIcons.mod,
};

String _plainBody(String value) {
  final withoutImages = value.replaceAll(RegExp(r'!\[[^\]]*\]\([^)]*\)'), '');
  final links = withoutImages.replaceAllMapped(
    RegExp(r'\[([^\]]+)\]\([^)]*\)'),
    (match) => match.group(1) ?? '',
  );
  final withoutTags = links.replaceAll(RegExp(r'<[^>]+>'), '');
  final withoutHeadings = withoutTags.replaceAll(
    RegExp(r'^#{1,6}\s*', multiLine: true),
    '',
  );
  return withoutHeadings.replaceAll(RegExp(r'(\*\*|__|~~|`)'), '').trim();
}

String _formatCount(int value) {
  if (value >= 1000000) return '${(value / 1000000).toStringAsFixed(1)}M';
  if (value >= 1000) return '${(value / 1000).toStringAsFixed(1)}K';
  return '$value';
}

String _versionTypeLabel(AppLocalizations l10n, String type) =>
    switch (type.toLowerCase()) {
      'release' => l10n.resourceVersionRelease,
      'beta' => l10n.resourceVersionBeta,
      'alpha' => l10n.resourceVersionAlpha,
      _ => type,
    };
