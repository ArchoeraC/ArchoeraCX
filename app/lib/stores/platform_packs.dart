import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/packs/platform_pack_models.dart';
import 'providers.dart';

/// Platform catalogues registered by the C# module.
///
/// The download page builds its source chips from this list instead of
/// hardcoding FTB/Technic/ATLauncher, so adding a provider only changes the
/// backend registry.
final platformPackProvidersProvider =
    FutureProvider<List<PlatformPackProviderInfo>>((ref) async {
      final client = ref.watch(coreClientProvider);
      if (client == null) return const [];
      return client.platformPackProviders();
    });
