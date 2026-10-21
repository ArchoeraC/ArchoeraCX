import 'package:flutter/material.dart';

import '../../models/account.dart';
import '../../theme/tokens.dart';

/// Visual mapping for account kinds. Kept out of the model so the model stays
/// locale- and theme-free.
extension AccountKindVisuals on AccountKind {
  Color colorFor(AppTokens tokens) => switch (this) {
    AccountKind.microsoft => tokens.success,
    AccountKind.offline => tokens.accent,
    AccountKind.authlib => tokens.warning,
  };

  IconData get icon => switch (this) {
    AccountKind.microsoft => Icons.window_rounded,
    AccountKind.offline => Icons.person_rounded,
    AccountKind.authlib => Icons.hub_rounded,
  };
}
