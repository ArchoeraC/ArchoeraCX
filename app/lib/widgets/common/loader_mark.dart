import 'package:flutter/material.dart';

import '../../models/game_instance.dart';

/// Loader identity mark.
///
/// All five marks are checked into `assets/icons/loaders/` so the UI uses
/// recognizable project/material artwork instead of abstract glyphs. Source,
/// license and trademark notes are in that directory's `SOURCES.md`.
class LoaderMark extends StatelessWidget {
  const LoaderMark({super.key, required this.loader, this.size = 24});

  final GameLoader loader;
  final double size;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: loader.name,
      image: true,
      child: SizedBox.square(
        dimension: size,
        child: Image.asset(
          _assetPath(loader),
          fit: BoxFit.contain,
          filterQuality: FilterQuality.none,
          errorBuilder: (context, error, stackTrace) => Icon(
            Icons.extension_rounded,
            size: size,
            color: Theme.of(context).colorScheme.primary,
          ),
        ),
      ),
    );
  }

  static String _assetPath(GameLoader loader) => switch (loader) {
    GameLoader.vanilla => 'assets/icons/loaders/vanilla.png',
    GameLoader.fabric => 'assets/icons/loaders/fabric.png',
    GameLoader.quilt => 'assets/icons/loaders/quilt.png',
    GameLoader.forge => 'assets/icons/loaders/forge.png',
    GameLoader.neoforge => 'assets/icons/loaders/neoforge.png',
  };
}
