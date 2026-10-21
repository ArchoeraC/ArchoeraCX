import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

class AppPageHeader extends StatelessWidget {
  const AppPageHeader({
    super.key,
    required this.title,
    this.subtitle,
    this.actions = const [],
  });

  final String title;
  final String? subtitle;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final titleBlock = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(title, style: textTheme.headlineSmall),
        if (subtitle != null) ...[
          const SizedBox(height: AppSpacing.xs),
          Text(
            subtitle!,
            style: textTheme.bodyMedium?.copyWith(color: tokens.textSecondary),
          ),
        ],
      ],
    );

    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth < 640 && actions.isNotEmpty) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              titleBlock,
              const SizedBox(height: AppSpacing.md),
              Wrap(
                spacing: AppSpacing.xs,
                runSpacing: AppSpacing.xs,
                children: actions,
              ),
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(child: titleBlock),
            if (actions.isNotEmpty) ...[
              const SizedBox(width: AppSpacing.md),
              Wrap(
                spacing: AppSpacing.xs,
                runSpacing: AppSpacing.xs,
                children: actions,
              ),
            ],
          ],
        );
      },
    );
  }
}
