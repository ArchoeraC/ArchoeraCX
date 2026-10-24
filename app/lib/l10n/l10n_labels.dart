import '../models/account.dart';
import '../models/game_instance.dart';
import '../models/launch_options.dart';
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

extension LaunchEnvironmentIssueL10n on LaunchEnvironmentIssue {
  String labelFor(AppLocalizations l10n) {
    final line = this.line ?? 0;
    return switch (kind) {
      LaunchEnvironmentIssueKind.missingSeparator =>
        l10n.launchEnvironmentMissingSeparator(line),
      LaunchEnvironmentIssueKind.invalidKey => l10n.launchEnvironmentInvalidKey(
        line,
        key ?? '',
      ),
      LaunchEnvironmentIssueKind.duplicateKey =>
        l10n.launchEnvironmentDuplicateKey(line, key ?? ''),
      LaunchEnvironmentIssueKind.reservedKey =>
        l10n.launchEnvironmentReservedKey(line, key ?? ''),
      LaunchEnvironmentIssueKind.tooManyEntries =>
        l10n.launchEnvironmentTooMany(launchEnvironmentMaxEntries),
      LaunchEnvironmentIssueKind.invalidValue =>
        l10n.launchEnvironmentInvalidValue(line),
    };
  }
}

extension LaunchCommandIssueL10n on LaunchCommandIssue {
  String labelFor(AppLocalizations l10n) => switch (kind) {
    LaunchCommandIssueKind.tooLong => l10n.launchCommandTooLong,
    LaunchCommandIssueKind.forbiddenCharacter =>
      l10n.launchCommandInvalidCharacter,
    LaunchCommandIssueKind.unclosedQuote => l10n.launchCommandUnclosedQuote(
      quote ?? '',
    ),
  };
}

extension AccountKindL10n on AccountKind {
  String labelFor(AppLocalizations l10n) => switch (this) {
    AccountKind.microsoft => l10n.accountMicrosoft,
    AccountKind.offline => l10n.accountOffline,
    AccountKind.authlib => l10n.accountAuthlib,
  };
}
