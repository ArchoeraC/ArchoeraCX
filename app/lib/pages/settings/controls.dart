part of '../settings_page.dart';

/// Persistent reminder shown while a stream count stays above the safe range.
class _HighConcurrencyNotice extends StatelessWidget {
  const _HighConcurrencyNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(AppIcons.warning, size: 16, color: tokens.warning),
        const SizedBox(width: AppSpacing.xs),
        Expanded(
          child: Text(
            message,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.warning),
          ),
        ),
      ],
    );
  }
}

/// Compact −/value/+ control used by numeric settings.
class _NumberStepper extends StatelessWidget {
  const _NumberStepper({
    super.key,
    required this.value,
    required this.min,
    required this.max,
    required this.onChanged,
  });

  final int value;
  final int min;
  final int max;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Container(
      padding: const EdgeInsets.all(2),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusM),
        border: Border.all(color: tokens.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          IconButton(
            onPressed: value > min ? () => onChanged(value - 1) : null,
            icon: const Icon(AppIcons.remove, size: 16),
            visualDensity: VisualDensity.compact,
            constraints: const BoxConstraints.tightFor(width: 28, height: 28),
            padding: EdgeInsets.zero,
          ),
          SizedBox(
            width: 32,
            child: Text(
              '$value',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.labelLarge,
            ),
          ),
          IconButton(
            onPressed: value < max ? () => onChanged(value + 1) : null,
            icon: const Icon(AppIcons.add, size: 16),
            visualDensity: VisualDensity.compact,
            constraints: const BoxConstraints.tightFor(width: 28, height: 28),
            padding: EdgeInsets.zero,
          ),
        ],
      ),
    );
  }
}

class _SegmentedOption<T> {
  const _SegmentedOption(this.value, this.label);

  final T value;
  final String label;
}

/// Compact segmented control with a flat track and an accent-tinted selection.
class _SegmentedControl<T> extends StatelessWidget {
  const _SegmentedControl({
    required this.options,
    required this.selected,
    required this.onChanged,
  });

  final List<_SegmentedOption<T>> options;
  final T selected;
  final ValueChanged<T> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusM),
        border: Border.all(color: tokens.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final option in options)
            Material(
              color: Colors.transparent,
              child: InkWell(
                borderRadius: BorderRadius.circular(tokens.radiusS),
                onTap: () => onChanged(option.value),
                child: AnimatedContainer(
                  duration: tokens.fast,
                  curve: tokens.curve,
                  padding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.sm,
                    vertical: 5,
                  ),
                  decoration: BoxDecoration(
                    color: option.value == selected
                        ? tokens.accent.withValues(alpha: 0.14)
                        : Colors.transparent,
                    borderRadius: BorderRadius.circular(tokens.radiusS),
                  ),
                  child: Text(
                    option.label,
                    maxLines: 1,
                    style: textTheme.labelMedium?.copyWith(
                      color: option.value == selected
                          ? tokens.accent
                          : tokens.textSecondary,
                      fontWeight: option.value == selected
                          ? FontWeight.w600
                          : FontWeight.w400,
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
