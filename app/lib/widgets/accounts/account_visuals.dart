import 'package:flutter/material.dart';

import '../../models/account.dart';
import '../../theme/icons/app_icons.dart';
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
    AccountKind.microsoft => AppIcons.microsoft,
    AccountKind.offline => AppIcons.accounts,
    AccountKind.authlib => AppIcons.thirdPartyAuth,
  };
}
