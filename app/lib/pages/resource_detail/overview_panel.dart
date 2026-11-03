part of '../resource_detail_page.dart';

/// Project overview: identity, statistics, gallery and the full description.
///
/// Page-level grouping follows the design system's flat sections: no filled
/// card, no shadow, one hairline rule below. The project page uses the full
/// content width, so wrapping these blocks in beveled panels made it read like
/// a dialog floating inside the download page.
class _ProjectOverview extends StatelessWidget {
  const _ProjectOverview({
    required this.project,
    required this.error,
    required this.onRetry,
  });

  final ResourceProject project;

  /// Non-null when the full record could not be loaded; the row data is still
  /// shown so the page never collapses back to an empty state.
  final String? error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final author = project.author?.trim();
    final body = project.body?.trim() ?? '';
    final gallery = project.gallery;

    return AppSection(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ResourceProjectThumb(project: project, size: 96),
              const SizedBox(width: AppSpacing.lg),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (author != null && author.isNotEmpty) ...[
                      Text(
                        l10n.downloadByAuthor(author),
                        style: textTheme.bodyMedium?.copyWith(
                          color: tokens.textSecondary,
                        ),
                      ),
                      const SizedBox(height: AppSpacing.xs),
                    ],
                    Wrap(
                      spacing: AppSpacing.sm,
                      runSpacing: AppSpacing.xs,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        ResourceMetaText(
                          icon: AppIcons.fileDownload,
                          text:
                              '${l10n.statDownloads}: '
                              '${formatResourceCount(project.downloads)}',
                        ),
                        ResourceMetaText(
                          icon: AppIcons.favorite,
                          text: formatResourceCount(project.follows),
                        ),
                        if (project.license?.trim().isNotEmpty == true)
                          ResourceTag(label: project.license!),
                        for (final loader in project.loaders.take(4))
                          ResourceTag(label: loader),
                      ],
                    ),
                    if (project.categories.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.xs),
                      Wrap(
                        spacing: AppSpacing.xxs,
                        runSpacing: AppSpacing.xxs,
                        children: [
                          for (final category in project.categories.take(8))
                            ResourceTag(label: category),
                        ],
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          if (error != null) ...[
            const SizedBox(height: AppSpacing.md),
            _OverviewError(message: error!, onRetry: onRetry),
          ],
          if (gallery.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.lg),
            Text(l10n.downloadScreenshots, style: textTheme.titleSmall),
            const SizedBox(height: AppSpacing.sm),
            _GalleryStrip(gallery: gallery),
          ],
          if (body.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.lg),
            Text(l10n.downloadDescription, style: textTheme.titleSmall),
            const SizedBox(height: AppSpacing.sm),
            SelectableText(
              resourcePlainBody(body),
              style: textTheme.bodyMedium?.copyWith(
                color: tokens.textSecondary,
                height: 1.55,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _OverviewError extends StatelessWidget {
  const _OverviewError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      padding: const EdgeInsets.all(AppSpacing.sm),
      decoration: BoxDecoration(
        color: tokens.danger.withValues(alpha: 0.08),
        border: Border.all(color: tokens.danger.withValues(alpha: 0.35)),
        borderRadius: BorderRadius.circular(tokens.radiusS),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(AppIcons.warning, size: 18, color: tokens.danger),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.downloadProjectLoadFailed,
                  style: textTheme.bodySmall?.copyWith(color: tokens.danger),
                ),
                const SizedBox(height: 2),
                Text(
                  message,
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
          TextButton(onPressed: onRetry, child: Text(l10n.refresh)),
        ],
      ),
    );
  }
}

/// Horizontal screenshot strip with desktop affordances.
///
/// Flutter only enables mouse dragging for touch-like pointers by default, and
/// a horizontal list ignores a vertical mouse wheel. This wrapper adds both,
/// plus arrow buttons, while keeping touch/trackpad behaviour unchanged.
class _GalleryStrip extends StatefulWidget {
  const _GalleryStrip({required this.gallery});

  final List<ResourceGalleryImage> gallery;

  @override
  State<_GalleryStrip> createState() => _GalleryStripState();
}

class _GalleryStripState extends State<_GalleryStrip> {
  static const double _cardWidth = 330;
  static const double _cardHeight = 220;
  static const double _gap = AppSpacing.sm;

  final _controller = ScrollController();
  Timer? _hideTimer;
  bool _canGoBack = false;
  bool _canGoForward = false;
  bool _arrowsVisible = false;

  @override
  void initState() {
    super.initState();
    _controller.addListener(_onScrollActivity);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _syncArrows();
      if (_canGoBack || _canGoForward) _showArrows();
    });
  }

  @override
  void didUpdateWidget(covariant _GalleryStrip oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.gallery.length != widget.gallery.length) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _syncArrows());
    }
  }

  @override
  void dispose() {
    _hideTimer?.cancel();
    _controller
      ..removeListener(_onScrollActivity)
      ..dispose();
    super.dispose();
  }

  void _onScrollActivity() {
    _syncArrows();
    _showArrows();
  }

  /// Shows the arrows and restarts the one-shot inactivity timer.
  ///
  /// This is activity-driven: no periodic timer or frame polling. The timer
  /// only fires once after three seconds without hover/scroll events.
  void _showArrows() {
    if (!mounted) return;
    if (!_arrowsVisible) {
      setState(() => _arrowsVisible = true);
    }

    _hideTimer?.cancel();
    _hideTimer = Timer(const Duration(seconds: 3), () {
      if (!mounted) return;
      setState(() => _arrowsVisible = false);
    });
  }

  void _onHover(PointerHoverEvent event) => _showArrows();

  void _syncArrows() {
    if (!mounted || !_controller.hasClients) return;
    final position = _controller.position;
    final back = position.pixels > 0.5;
    final forward = position.pixels < position.maxScrollExtent - 0.5;
    if (back != _canGoBack || forward != _canGoForward) {
      setState(() {
        _canGoBack = back;
        _canGoForward = forward;
      });
    }
  }

  void _scrollBy(double delta) {
    if (!_controller.hasClients) return;
    final target = (_controller.offset + delta).clamp(
      0.0,
      _controller.position.maxScrollExtent,
    );
    _controller.animateTo(
      target,
      duration: const Duration(milliseconds: 180),
      curve: Curves.easeOutCubic,
    );
  }

  void _onPointerSignal(PointerSignalEvent event) {
    if (event is! PointerScrollEvent || !_controller.hasClients) return;
    final delta = _scrollDelta(event);
    if (delta == 0) return;

    // Let the page scroll when the strip is already at the requested edge;
    // otherwise claim the pointer signal so the outer vertical page does not
    // scroll at the same time.
    final position = _controller.position;
    if (delta > 0 && position.pixels >= position.maxScrollExtent - 0.5) return;
    if (delta < 0 && position.pixels <= 0.5) return;

    GestureBinding.instance.pointerSignalResolver.register(event, (signal) {
      if (signal is! PointerScrollEvent) return;
      _scrollBy(_scrollDelta(signal));
    });
  }

  static double _scrollDelta(PointerScrollEvent event) =>
      event.scrollDelta.dx != 0 ? event.scrollDelta.dx : event.scrollDelta.dy;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final material = MaterialLocalizations.of(context);
    final arrowsVisible = _arrowsVisible;
    final behavior = ScrollConfiguration.of(context).copyWith(
      dragDevices: {
        PointerDeviceKind.touch,
        PointerDeviceKind.mouse,
        PointerDeviceKind.trackpad,
        PointerDeviceKind.stylus,
        PointerDeviceKind.unknown,
      },
    );

    return SizedBox(
      height: _cardHeight,
      child: MouseRegion(
        onEnter: (_) => _showArrows(),
        onHover: _onHover,
        onExit: (_) => _showArrows(),
        child: Stack(
          children: [
            ScrollConfiguration(
              behavior: behavior,
              child: Listener(
                onPointerSignal: _onPointerSignal,
                child: Scrollbar(
                  controller: _controller,
                  interactive: true,
                  thumbVisibility: true,
                  thickness: 4,
                  radius: const Radius.circular(4),
                  scrollbarOrientation: ScrollbarOrientation.bottom,
                  child: ListView.separated(
                    controller: _controller,
                    scrollDirection: Axis.horizontal,
                    itemCount: widget.gallery.length,
                    separatorBuilder: (_, _) => const SizedBox(width: _gap),
                    itemBuilder: (context, index) {
                      final image = widget.gallery[index];
                      return Semantics(
                        label: l10n.downloadImagePreview,
                        button: true,
                        child: InkWell(
                          borderRadius: BorderRadius.circular(tokens.radiusM),
                          onTap: () => showResourceImagePreview(context, image),
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(tokens.radiusM),
                            child: SizedBox(
                              width: _cardWidth,
                              height: _cardHeight,
                              child: AppNetworkImage(
                                url: image.url,
                                fallbackIcon: AppIcons.imagePlaceholder,
                              ),
                            ),
                          ),
                        ),
                      );
                    },
                  ),
                ),
              ),
            ),
            if (_canGoBack)
              _GalleryArrow(
                alignment: Alignment.centerLeft,
                flip: true,
                icon: AppIcons.chevronRight,
                tooltip: material.previousPageTooltip,
                visible: arrowsVisible,
                onPressed: () => _scrollBy(-(_cardWidth + _gap)),
              ),
            if (_canGoForward)
              _GalleryArrow(
                alignment: Alignment.centerRight,
                icon: AppIcons.chevronRight,
                tooltip: material.nextPageTooltip,
                visible: arrowsVisible,
                onPressed: () => _scrollBy(_cardWidth + _gap),
              ),
          ],
        ),
      ),
    );
  }
}

class _GalleryArrow extends StatelessWidget {
  const _GalleryArrow({
    required this.alignment,
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.flip = false,
    this.visible = true,
  });

  final Alignment alignment;
  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;

  /// Mirrors the same glyph for the previous-page side.
  final bool flip;

  /// Fades the control out while keeping it in the tree for a cheap animation.
  final bool visible;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Align(
      alignment: alignment,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
        child: AnimatedOpacity(
          opacity: visible ? 1 : 0,
          duration: const Duration(milliseconds: 160),
          curve: Curves.easeOutCubic,
          child: IgnorePointer(
            ignoring: !visible,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(tokens.radiusM),
              child: BackdropFilter(
                filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
                child: Material(
                  color: tokens.surface1.withValues(alpha: 0.58),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(tokens.radiusM),
                    side: BorderSide(
                      color: tokens.border.withValues(alpha: 0.85),
                    ),
                  ),
                  child: IconButton(
                    onPressed: onPressed,
                    tooltip: tooltip,
                    iconSize: 20,
                    icon: Transform.scale(
                      scaleX: flip ? -1 : 1,
                      child: Icon(icon, color: tokens.textPrimary),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
