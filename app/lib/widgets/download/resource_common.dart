/// Shared building blocks for the download catalogue and its project page.
///
/// The search list and the full-page project view render the same provider
/// records. Keeping the thumb, metadata atoms and text normalization here
/// stops the two surfaces from drifting apart.
library;

import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/resources/resource_models.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_network_image.dart';

/// Content-type glyph for `mod`, `modpack`, `resourcepack` and `shader`.
IconData resourceKindIcon(String kind) => switch (kind) {
  'shader' => AppIcons.shader,
  'resourcepack' => AppIcons.resourcePack,
  'modpack' => AppIcons.modpack,
  _ => AppIcons.mod,
};

/// Modrinth descriptions are Markdown; the launcher shows readable plain text
/// instead of rendering arbitrary HTML or remote images inline.
String resourcePlainBody(String value) {
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

String formatResourceCount(int value) {
  if (value >= 1000000) return '${(value / 1000000).toStringAsFixed(1)}M';
  if (value >= 1000) return '${(value / 1000).toStringAsFixed(1)}K';
  return '$value';
}

String resourceVersionTypeLabel(AppLocalizations l10n, String type) =>
    switch (type.toLowerCase()) {
      'release' => l10n.resourceVersionRelease,
      'beta' => l10n.resourceVersionBeta,
      'alpha' => l10n.resourceVersionAlpha,
      _ => type,
    };

/// Installation target shown next to a version list.
///
/// This is the instance the chosen version will be written to, so it returns
/// the "select an instance" hint when nothing is selected. Modpacks create
/// their own instance.
String resourceInstallTarget(
  AppLocalizations l10n, {
  required String kind,
  required GameInstance? instance,
}) {
  if (kind == 'modpack') return l10n.packTargetNewInstance;
  if (instance == null) return l10n.selectInstance;
  final loader = instance.loader.labelFor(l10n);
  return switch (kind) {
    'shader' => l10n.resourceTargetShader(instance.gameVersion),
    'resourcepack' => l10n.resourceTargetGame(instance.gameVersion),
    _ => l10n.resourceTargetGameLoader(instance.gameVersion, loader),
  };
}

/// Provider gallery thumbnail with a content-type fallback.
class ResourceProjectThumb extends StatelessWidget {
  const ResourceProjectThumb({
    super.key,
    required this.project,
    required this.size,
  });

  final ResourceProject project;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final icon = resourceKindIcon(project.kind);
    final url = project.iconUrl;
    return ClipRRect(
      borderRadius: BorderRadius.circular(tokens.radiusM),
      child: SizedBox(
        width: size,
        height: size,
        child: url == null || url.isEmpty
            ? ColoredBox(
                color: tokens.surface2,
                child: Icon(icon, color: tokens.accent, size: size * 0.44),
              )
            : AppNetworkImage(url: url, fallbackIcon: icon),
      ),
    );
  }
}

class ResourceMetaText extends StatelessWidget {
  const ResourceMetaText({
    super.key,
    required this.icon,
    required this.text,
  });

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 13, color: tokens.textTertiary),
        const SizedBox(width: 3),
        Text(
          text,
          style: Theme.of(context).textTheme.labelSmall?.copyWith(
            color: tokens.textTertiary,
          ),
        ),
      ],
    );
  }
}

class ResourceTag extends StatelessWidget {
  const ResourceTag({super.key, required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusS),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: tokens.textSecondary,
        ),
      ),
    );
  }
}

/// Opens a provider gallery image at window size with pinch/scroll zoom.
Future<void> showResourceImagePreview(
  BuildContext context,
  ResourceGalleryImage image,
) {
  return showDialog<void>(
    context: context,
    barrierColor: Colors.black.withValues(alpha: 0.88),
    builder: (context) => Dialog(
      backgroundColor: Colors.black,
      insetPadding: const EdgeInsets.all(AppSpacing.lg),
      child: SizedBox(
        width: MediaQuery.sizeOf(context).width * 0.9,
        height: MediaQuery.sizeOf(context).height * 0.86,
        child: Stack(
          children: [
            Positioned.fill(
              child: InteractiveViewer(
                minScale: 0.6,
                maxScale: 5,
                child: SizedBox.expand(
                  child: AppNetworkImage(
                    url: image.url,
                    fit: BoxFit.contain,
                    fallbackIcon: AppIcons.imagePlaceholder,
                  ),
                ),
              ),
            ),
            Positioned(
              top: 4,
              right: 4,
              child: IconButton(
                onPressed: () => Navigator.of(context).pop(),
                tooltip: MaterialLocalizations.of(context).closeButtonTooltip,
                icon: const Icon(AppIcons.close, color: Colors.white),
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
