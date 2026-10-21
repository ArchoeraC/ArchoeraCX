import '../models/account.dart';
import '../models/game_instance.dart';
import 'generated/app_localizations.dart';

/// Standard UI labels for enum values. Models stay locale-free; widgets turn
/// stable wire values into translated labels through these extensions.
extension GameLoaderL10n on GameLoader {
  String labelFor(AppLocalizations l10n) => switch (this) {
    GameLoader.vanilla => l10n.loaderVanilla,
    GameLoader.fabric => l10n.loaderFabric,
    GameLoader.quilt => l10n.loaderQuilt,
    GameLoader.forge => l10n.loaderForge,
    GameLoader.neoforge => l10n.loaderNeoForge,
  };
}

extension GameInstanceL10n on GameInstance {
  String loaderLabelFor(AppLocalizations l10n) =>
      loader == GameLoader.vanilla || loaderVersion.isEmpty
      ? loader.labelFor(l10n)
      : l10n.loaderVersionValue(loader.labelFor(l10n), loaderVersion);
}

extension AccountKindL10n on AccountKind {
  String labelFor(AppLocalizations l10n) => switch (this) {
    AccountKind.microsoft => l10n.accountMicrosoft,
    AccountKind.offline => l10n.accountOffline,
    AccountKind.authlib => l10n.accountAuthlib,
  };
}
