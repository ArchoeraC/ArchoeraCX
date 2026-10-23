part of '../settings_page.dart';

// ---------------------------------------------------------------------------
// Accent color helpers
// ---------------------------------------------------------------------------

const _accentPresets = <Color>[
  Color(0xFF4F7CFF),
  Color(0xFF7A9BFF),
  Color(0xFF9B8CFF),
  Color(0xFFFF6B9D),
  Color(0xFFFF6B61),
  Color(0xFFFFA24D),
  Color(0xFF4DDB9B),
  Color(0xFF4DD8E0),
];

String _hexColor(Color color) {
  final value = color.toARGB32() & 0xFFFFFF;
  return '#${value.toRadixString(16).padLeft(6, '0').toUpperCase()}';
}

Color? _parseHexColor(String raw) {
  var text = raw.trim();
  if (text.startsWith('#')) text = text.substring(1);
  if (text.length != 6 && text.length != 8) return null;
  final value = int.tryParse(text, radix: 16);
  if (value == null) return null;
  return Color(text.length == 8 ? value : 0xFF000000 | value);
}

/// Preset swatches plus a picker for a custom hex color.
class _AccentSwatchRow extends StatelessWidget {
  const _AccentSwatchRow({
    required this.current,
    required this.onPick,
    required this.onChoose,
  });

  final Color current;
  final ValueChanged<Color> onPick;
  final VoidCallback onChoose;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.sm,
        0,
        AppSpacing.sm,
        AppSpacing.sm,
      ),
      child: Row(
        children: [
          Expanded(
            child: Wrap(
              spacing: AppSpacing.xs,
              runSpacing: AppSpacing.xs,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                for (final color in _accentPresets)
                  _AccentSwatch(
                    color: color,
                    selected: color.toARGB32() == current.toARGB32(),
                    onTap: () => onPick(color),
                  ),
                Tooltip(
                  message: _hexColor(current),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(26),
                    onTap: onChoose,
                    child: Container(
                      width: 26,
                      height: 26,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: tokens.surface2,
                        border: Border.all(color: tokens.borderStrong),
                      ),
                      child: Icon(
                        AppIcons.colorPicker,
                        size: 14,
                        color: tokens.textSecondary,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          Text(
            _hexColor(current),
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textTertiary,
              letterSpacing: 0,
            ),
          ),
        ],
      ),
    );
  }
}

/// Read-only swatch shown when the launcher follows the desktop accent.
class _SystemAccentRow extends StatelessWidget {
  const _SystemAccentRow({required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.sm,
        0,
        AppSpacing.sm,
        AppSpacing.sm,
      ),
      child: Row(
        children: [
          Container(
            width: 22,
            height: 22,
            decoration: BoxDecoration(
              color: color,
              shape: BoxShape.circle,
              border: Border.all(color: tokens.borderStrong),
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          const Spacer(),
          Text(
            _hexColor(color),
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textTertiary,
              letterSpacing: 0,
            ),
          ),
        ],
      ),
    );
  }
}

class _AccentSwatch extends StatelessWidget {
  const _AccentSwatch({
    required this.color,
    required this.selected,
    required this.onTap,
    this.size = 26,
  });

  final Color color;
  final bool selected;
  final VoidCallback onTap;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Tooltip(
      message: _hexColor(color),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(size),
        child: Container(
          width: size,
          height: size,
          decoration: BoxDecoration(
            color: color,
            shape: BoxShape.circle,
            border: Border.all(
              color: selected ? tokens.textPrimary : tokens.border,
              width: selected ? 2 : 1,
            ),
          ),
          child: selected
              ? Icon(
                  AppIcons.check,
                  size: size * 0.6,
                  color: color.computeLuminance() > 0.55
                      ? const Color(0xFF0B1020)
                      : Colors.white,
                )
              : null,
        ),
      ),
    );
  }
}
