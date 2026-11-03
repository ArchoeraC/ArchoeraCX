part of '../accounts_page.dart';

/// Third-party Yggdrasil sign-in.
///
/// The terms panel is shown before the request: C# refuses the login without
/// `acceptedTerms: true`, and only then downloads authlib-injector.
Future<Account?> showAuthlibLoginDialog(BuildContext context, WidgetRef ref) {
  return showDialog<Account>(
    context: context,
    builder: (context) => const _AuthlibLoginDialog(),
  );
}

class _AuthlibLoginDialog extends ConsumerStatefulWidget {
  const _AuthlibLoginDialog();

  @override
  ConsumerState<_AuthlibLoginDialog> createState() =>
      _AuthlibLoginDialogState();
}

class _AuthlibLoginDialogState extends ConsumerState<_AuthlibLoginDialog> {
  static const _presets = <({String name, String server, String terms})>[
    (
      name: 'LittleSkin',
      server: 'https://littleskin.cn/api/yggdrasil',
      terms: 'https://manual.littleskin.cn/policies/tos',
    ),
    (
      name: 'Blessing Skin',
      server: 'https://skin.prinzeugen.net/api/yggdrasil',
      terms: 'https://skin.prinzeugen.net/terms-and-conditions/',
    ),
    (
      name: 'Ely.by',
      server: 'https://account.ely.by/api/authlib-injector',
      terms: 'https://ely.by/site/rules',
    ),
  ];

  final _server = TextEditingController();
  final _terms = TextEditingController();
  final _username = TextEditingController();
  final _password = TextEditingController();
  String _preset = 'LittleSkin';
  bool _custom = false;
  bool _accepted = false;
  bool _busy = false;
  bool _openedTerms = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _applyPreset('LittleSkin');
  }

  @override
  void dispose() {
    _server.dispose();
    _terms.dispose();
    _username.dispose();
    _password.dispose();
    super.dispose();
  }

  void _applyPreset(String name) {
    ({String name, String server, String terms})? preset;
    for (final item in _presets) {
      if (item.name == name) {
        preset = item;
        break;
      }
    }

    if (preset == null) {
      _custom = true;
      _server.clear();
      _terms.clear();
      return;
    }
    _custom = false;
    _server.text = preset.server;
    _terms.text = preset.terms;
  }

  bool get _canSubmit =>
      !_busy &&
      _accepted &&
      _server.text.trim().isNotEmpty &&
      _terms.text.trim().isNotEmpty &&
      _username.text.trim().isNotEmpty &&
      _password.text.isNotEmpty;

  Future<void> _importLink() async {
    final l10n = AppLocalizations.of(context);
    final raw = (await Clipboard.getData(Clipboard.kTextPlain))?.text?.trim();
    final server = raw == null ? null : parseAuthlibServerLink(raw);
    if (server == null || server.isEmpty) {
      setState(() => _error = l10n.accountAuthImportFailed);
      return;
    }

    ({String name, String server, String terms})? match;
    for (final item in _presets) {
      if (item.server.toLowerCase() ==
          server.toLowerCase().replaceAll(RegExp(r'/+$'), '')) {
        match = item;
        break;
      }
    }

    setState(() {
      _error = null;
      _accepted = false;
      _openedTerms = false;
      if (match != null) {
        _preset = match.name;
        _custom = false;
        _server.text = match.server;
        _terms.text = match.terms;
      } else {
        _preset = 'custom';
        _custom = true;
        _server.text = server;
        _terms.clear();
      }
    });
  }

  Future<void> _openTerms() async {
    final opened = await launchWebUrl(
      _terms.text.trim(),
      mode: LaunchMode.externalApplication,
    );
    if (!mounted) return;
    if (opened) {
      setState(() => _openedTerms = true);
    } else {
      setState(
        () => _error = AppLocalizations.of(context).accountAuthTermsOpenFailed,
      );
    }
  }

  Future<void> _submit() async {
    if (!_canSubmit) return;
    final l10n = AppLocalizations.of(context);
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() => _error = l10n.commonCoreNotConnected);
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final policy = ref.read(accountPolicyProvider);
      final account = await client.loginAuthlib(
        server: _server.text.trim(),
        username: _username.text.trim(),
        password: _password.text,
        termsUrl: _terms.text.trim(),
        acceptedTerms: _accepted,
        requireMicrosoft: policy.microsoftOnly,
      );
      await ref.read(accountsProvider.notifier).reload();
      if (!mounted) return;
      Navigator.of(context).pop(account);
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = _describeAuthlibError(l10n, error);
      });
    }
  }

  String _describeAuthlibError(AppLocalizations l10n, Object error) {
    if (error is CoreException) {
      return switch (error.code) {
        'AUTHLIB_TERMS_REQUIRED' => l10n.accountAuthTermsRequired,
        'AUTHLIB_SERVER_INVALID' => l10n.accountAuthServerInvalid,
        'AUTHLIB_INVALID_CREDENTIALS' => l10n.accountAuthInvalidCredentials,
        'AUTHLIB_NO_PROFILE' => l10n.accountAuthNoProfile,
        'AUTHLIB_NETWORK' => l10n.accountAuthNetwork,
        'AUTHLIB_JAR_DOWNLOAD_FAILED' => l10n.accountAuthDownloadFailed,
        _ => error.message,
      };
    }
    return error.toString();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final termsOk = tryParseWebUrl(_terms.text.trim()) != null;
    return AlertDialog(
      title: Text(l10n.accountAuthTitle),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              AppDropdownFormField<String>(
                initialValue: _custom ? 'custom' : _preset,
                decoration: InputDecoration(
                  labelText: l10n.accountAuthServerPreset,
                ),
                items: [
                  for (final preset in _presets)
                    DropdownMenuItem(
                      value: preset.name,
                      child: Text(preset.name),
                    ),
                  DropdownMenuItem(
                    value: 'custom',
                    child: Text(l10n.accountAuthCustomServer),
                  ),
                ],
                onChanged: _busy
                    ? null
                    : (value) {
                        if (value == null) return;
                        setState(() {
                          _preset = value;
                          _accepted = false;
                          _openedTerms = false;
                          _applyPreset(value);
                        });
                      },
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _server,
                enabled: !_busy && _custom,
                decoration: InputDecoration(
                  labelText: l10n.accountAuthServerUrl,
                  hintText: 'https://example.com/api/yggdrasil',
                ),
                onChanged: (_) => setState(() {}),
              ),
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: _busy ? null : _importLink,
                  icon: const Icon(AppIcons.externalLink, size: 16),
                  label: Text(l10n.accountAuthImportLink),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _terms,
                enabled: !_busy && _custom,
                decoration: InputDecoration(
                  labelText: l10n.accountAuthTermsUrl,
                  hintText: 'https://example.com/terms',
                ),
                onChanged: (_) => setState(() {
                  _accepted = false;
                  _openedTerms = false;
                }),
              ),
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  border: Border.all(color: tokens.border),
                  color: tokens.surface1,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      l10n.accountAuthTermsTitle,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      l10n.accountAuthTermsBody(_server.text.trim()),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const SizedBox(height: 6),
                    SelectableText(
                      _terms.text.trim(),
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                    const SizedBox(height: 6),
                    TextButton.icon(
                      onPressed: _busy || !termsOk ? null : _openTerms,
                      icon: const Icon(AppIcons.openExternal, size: 16),
                      label: Text(l10n.accountAuthOpenTerms),
                    ),
                    CheckboxListTile(
                      contentPadding: EdgeInsets.zero,
                      value: _accepted,
                      onChanged: _busy || !_openedTerms
                          ? null
                          : (value) =>
                                setState(() => _accepted = value ?? false),
                      title: Text(l10n.accountAuthAcceptTerms),
                      subtitle: _openedTerms
                          ? null
                          : Text(l10n.accountAuthOpenTermsFirst),
                      controlAffinity: ListTileControlAffinity.leading,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _username,
                enabled: !_busy,
                decoration: InputDecoration(
                  labelText: l10n.accountAuthUsername,
                ),
                textInputAction: TextInputAction.next,
                onChanged: (_) => setState(() {}),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _password,
                enabled: !_busy,
                obscureText: true,
                decoration: InputDecoration(
                  labelText: l10n.accountAuthPassword,
                ),
                onChanged: (_) => setState(() {}),
                onSubmitted: (_) => _submit(),
              ),
              if (_error != null) ...[
                const SizedBox(height: 12),
                Text(_error!, style: TextStyle(color: tokens.danger)),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy ? null : () => Navigator.of(context).pop(),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(
          onPressed: _canSubmit ? _submit : null,
          child: _busy
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(l10n.accountAuthLogin),
        ),
      ],
    );
  }
}
