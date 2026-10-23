part of '../settings_page.dart';

// ---------------------------------------------------------------------------
// About
// ---------------------------------------------------------------------------

const _repoUrl = 'https://github.com/BetaStudio2/ArchoeraCX';
const _issuesUrl = 'https://github.com/BetaStudio2/ArchoeraCX/issues';
const _privacyUrl =
    'https://github.com/BetaStudio2/ArchoeraCX/blob/main/PRIVACY.md';
const _securityUrl =
    'https://github.com/BetaStudio2/ArchoeraCX/blob/main/SECURITY.md';
const _licenseUrl =
    'https://github.com/BetaStudio2/ArchoeraCX/blob/main/LICENSE';

const _thanks = <(String, String, String)>[
  ('Flutter', 'flutter.dev', 'https://flutter.dev'),
  ('.NET', 'dotnet.microsoft.com', 'https://dotnet.microsoft.com'),
  ('Rust', 'rust-lang.org', 'https://www.rust-lang.org'),
  ('Modrinth', 'modrinth.com', 'https://modrinth.com'),
  ('BMCLAPI', 'bmclapi2.bangbang93.com', 'https://bmclapi2.bangbang93.com'),
  (
    'Noto Sans SC',
    'Google Fonts',
    'https://fonts.google.com/noto/specimen/Noto+Sans+SC',
  ),
  (
    'Tabler Icons',
    'Paweł Kuna · MIT',
    'https://github.com/tabler/tabler-icons',
  ),
];

Map<String, Object?>? _parseBuildInfo(String? raw) {
  if (raw == null || raw.isEmpty) return null;
  try {
    final decoded = jsonDecode(raw);
    return decoded is Map<String, Object?> ? decoded : null;
  } on FormatException {
    return null;
  }
}

class _AboutPanel extends ConsumerWidget {
  const _AboutPanel({this.settings});

  final AppSettings? settings;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final client = ref.watch(coreClientProvider);
    final build = _parseBuildInfo(client?.buildInfo);
    final coreLabel = build == null
        ? l10n.settingsAboutCoreUnknown
        : '${build['name'] ?? 'archoera_core'} '
              '${build['version'] ?? '—'} '
              '(ABI ${build['abiVersion'] ?? '—'})';
    final environment = <String>[
      'ArchoeraCX ${AppInfo.version}',
      '${l10n.settingsAboutPlatform}: ${Platform.operatingSystem} '
          '${Platform.operatingSystemVersion}',
      '${l10n.settingsAboutRuntime}: Dart ${Platform.version.split(' ').first}',
      '${l10n.settingsAboutCore}: $coreLabel',
      if (settings?.dataRoot != null)
        '${l10n.settingsDataRoot}: ${settings!.dataRoot}',
    ].join('\n');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SettingsCard(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.xs,
                AppSpacing.sm,
                AppSpacing.xs,
                AppSpacing.sm,
              ),
              child: Row(
                children: [
                  Container(
                    width: 64,
                    height: 64,
                    decoration: BoxDecoration(
                      color: tokens.surface2,
                      borderRadius: BorderRadius.circular(tokens.radiusM),
                      border: Border.all(color: tokens.border),
                    ),
                    padding: const EdgeInsets.all(AppSpacing.xs),
                    child: Image.asset(
                      'assets/branding/archoera_icon_256.png',
                      fit: BoxFit.contain,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'ArchoeraCX',
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          l10n.settingsAboutTagline,
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: tokens.textSecondary),
                        ),
                        const SizedBox(height: AppSpacing.xs),
                        Wrap(
                          spacing: AppSpacing.xs,
                          runSpacing: AppSpacing.xxs,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            _InfoChip(
                              label: AppInfo.version,
                              color: tokens.accent,
                              background: tokens.accentSoft,
                            ),
                            if (AppInfo.isDevelopmentBuild)
                              _InfoChip(
                                label: l10n.developmentBuildBadge,
                                color: tokens.warning,
                                background: tokens.warning.withValues(
                                  alpha: 0.14,
                                ),
                              ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.xs,
                0,
                AppSpacing.xs,
                AppSpacing.sm,
              ),
              child: Text(
                l10n.settingsAboutDesc,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutEnvironment),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _AboutEnvRow(
              icon: AppIcons.version,
              label: l10n.settingsVersion,
              value: AppInfo.version,
            ),
            _AboutEnvRow(
              icon: AppIcons.platform,
              label: l10n.settingsAboutPlatform,
              value:
                  '${Platform.operatingSystem} ${Platform.operatingSystemVersion}',
            ),
            _AboutEnvRow(
              icon: AppIcons.memory,
              label: l10n.settingsAboutRuntime,
              value: 'Dart ${Platform.version.split(' ').first}',
            ),
            _AboutEnvRow(
              icon: AppIcons.core,
              label: l10n.settingsAboutCore,
              value: coreLabel,
            ),
            if (settings?.dataRoot != null)
              _AboutEnvRow(
                icon: AppIcons.storage,
                label: l10n.settingsDataRoot,
                value: settings!.dataRoot,
              ),
            const _UpdateCheckRow(),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.xs,
                AppSpacing.xxs,
                AppSpacing.xs,
                AppSpacing.sm,
              ),
              child: Align(
                alignment: Alignment.centerRight,
                child: TextButton.icon(
                  onPressed: () {
                    Clipboard.setData(ClipboardData(text: environment));
                    _notify(
                      context,
                      l10n.settingsAboutCopied,
                      type: ToastType.success,
                    );
                  },
                  icon: const Icon(AppIcons.copy, size: 17),
                  label: Text(l10n.settingsAboutCopyEnvironment),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutCommunity),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _AboutLinkRow(
              icon: AppIcons.repository,
              title: l10n.settingsAboutRepository,
              value: 'github.com/BetaStudio2/ArchoeraCX',
              onTap: () => _openExternal(context, l10n, _repoUrl),
            ),
            _AboutLinkRow(
              icon: AppIcons.issues,
              title: l10n.settingsAboutIssues,
              value: 'github.com/BetaStudio2/ArchoeraCX/issues',
              onTap: () => _openExternal(context, l10n, _issuesUrl),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutThanks),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            for (final item in _thanks)
              _AboutLinkRow(
                icon: AppIcons.credits,
                title: item.$1,
                value: item.$2,
                onTap: () => _openExternal(context, l10n, item.$3),
              ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutLegal),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _AboutLinkRow(
              icon: AppIcons.license,
              title: l10n.settingsLicense,
              value: 'AGPL-3.0-only',
              onTap: () => _openExternal(context, l10n, _licenseUrl),
            ),
            _AboutLinkRow(
              icon: AppIcons.thirdParty,
              title: l10n.settingsAboutThirdParty,
              value: l10n.settingsAboutThirdPartyDesc,
              onTap: () => showLicensePage(
                context: context,
                applicationName: 'ArchoeraCX',
                applicationVersion: AppInfo.version,
                applicationLegalese: 'AGPL-3.0-only',
                applicationIcon: Image.asset(
                  'assets/branding/archoera_icon_256.png',
                  width: 48,
                  height: 48,
                ),
              ),
            ),
            _AboutLinkRow(
              icon: AppIcons.privacy,
              title: l10n.settingsAboutPrivacy,
              value: 'PRIVACY.md',
              onTap: () => _openExternal(context, l10n, _privacyUrl),
            ),
            _AboutLinkRow(
              icon: AppIcons.security,
              title: l10n.settingsAboutSecurity,
              value: 'SECURITY.md',
              onTap: () => _openExternal(context, l10n, _securityUrl),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
          child: Text(
            l10n.settingsAboutDisclaimer,
            style: Theme.of(context).textTheme.labelSmall
                ?.copyWith(color: tokens.textTertiary, letterSpacing: 0.2),
          ),
        ),
      ],
    );
  }

  Future<void> _openExternal(
    BuildContext context,
    AppLocalizations l10n,
    String url,
  ) async {
    // Even though these URLs are compile-time constants, the opener still
    // goes through the shared http/https whitelist.
    var opened = false;
    try {
      opened = await launchWebUrl(url, mode: LaunchMode.externalApplication);
    } catch (_) {
      opened = false;
    }
    if (!opened && context.mounted) {
      _notify(context, l10n.settingsAboutOpenFailed, type: ToastType.error);
    }
  }
}

class _AboutSectionLabel extends StatelessWidget {
  const _AboutSectionLabel({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(left: AppSpacing.xxs),
      child: Text(
        text.toUpperCase(),
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(letterSpacing: 1.0, color: context.tokens.textTertiary),
      ),
    );
  }
}

class _InfoChip extends StatelessWidget {
  const _InfoChip({
    required this.label,
    required this.color,
    required this.background,
  });

  final String label;
  final Color color;
  final Color background;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: 2,
      ),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(context.tokens.radiusS),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(color: color),
      ),
    );
  }
}

class _AboutEnvRow extends StatelessWidget {
  const _AboutEnvRow({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xs,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: tokens.textSecondary),
          const SizedBox(width: AppSpacing.xs),
          SizedBox(
            width: 112,
            child: Text(
              label,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textSecondary),
            ),
          ),
          Expanded(
            child: SelectableText(
              value,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        ],
      ),
    );
  }
}

class _AboutLinkRow extends StatelessWidget {
  const _AboutLinkRow({
    required this.icon,
    required this.title,
    required this.value,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String value;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
      onTap: onTap,
      leading: Icon(icon, size: 20, color: tokens.textSecondary),
      title: Text(title, style: Theme.of(context).textTheme.bodyMedium),
      subtitle: Text(
        value,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: Theme.of(context).textTheme.bodySmall
            ?.copyWith(color: tokens.textTertiary),
      ),
      trailing: Icon(
        AppIcons.externalLink,
        size: 16,
        color: tokens.textTertiary,
      ),
    );
  }
}
