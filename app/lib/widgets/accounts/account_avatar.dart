import 'package:flutter/material.dart';

import '../../models/account.dart';
import '../../theme/tokens.dart';
import 'account_visuals.dart';

/// Angular account avatar: a bevel-cut square rather than a generic circle.
///
/// Falls back to the account initial (skin images are the only UI-owned
/// network fetch, matching the rest of the launcher).
class AccountAvatar extends StatelessWidget {
  const AccountAvatar({super.key, required this.account, this.size = 44});

  final Account account;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final shape = BeveledRectangleBorder(
      side: BorderSide(color: tokens.border),
      borderRadius: BorderRadius.only(
        topRight: Radius.circular(size * 0.26),
        bottomLeft: Radius.circular(size * 0.16),
      ),
    );
    final color = account.kind.colorFor(tokens);
    final fallback = Container(
      width: size,
      height: size,
      color: tokens.surface2,
      alignment: Alignment.center,
      child: Text(
        account.displayName.characters.first.toUpperCase(),
        style: TextStyle(
          color: color,
          fontSize: size * 0.38,
          fontWeight: FontWeight.w500,
        ),
      ),
    );

    final skinUrl = account.skinUrl;
    final content = skinUrl == null
        ? fallback
        : Image.network(
            skinUrl,
            width: size,
            height: size,
            fit: BoxFit.cover,
            semanticLabel: account.displayName,
            errorBuilder: (context, error, stackTrace) => fallback,
          );

    return SizedBox(
      width: size,
      height: size,
      child: DecoratedBox(
        decoration: ShapeDecoration(shape: shape),
        child: ClipPath(
          clipper: ShapeBorderClipper(shape: shape),
          child: content,
        ),
      ),
    );
  }
}

/// Small square mark used for empty states and account-type actions.
class AccountMark extends StatelessWidget {
  const AccountMark({
    super.key,
    required this.icon,
    this.color,
    this.size = 44,
  });

  final IconData icon;
  final Color? color;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final shape = BeveledRectangleBorder(
      borderRadius: BorderRadius.only(
        topRight: Radius.circular(size * 0.26),
        bottomLeft: Radius.circular(size * 0.16),
      ),
    );
    final foreground = color ?? tokens.accent;
    return Container(
      width: size,
      height: size,
      decoration: ShapeDecoration(
        color: foreground.withValues(alpha: 0.12),
        shape: shape,
      ),
      alignment: Alignment.center,
      child: Icon(icon, size: size * 0.5, color: foreground),
    );
  }
}
