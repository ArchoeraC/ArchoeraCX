import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/stores/instances.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  ProviderContainer container() {
    final value = ProviderContainer(
      overrides: [coreClientProvider.overrideWithValue(null)],
    );
    addTearDown(value.dispose);
    return value;
  }

  test('creates an instance and selects it', () async {
    final provider = container();

    final before = (await provider.read(instancesProvider.future)).length;
    await provider
        .read(instancesProvider.notifier)
        .create(
          name: ' Test Instance ',
          gameVersion: '1.21.4',
          loader: GameLoader.neoforge,
        );

    final instances = await provider.read(instancesProvider.future);
    expect(instances.length, before + 1);
    expect(instances.first.name, 'Test Instance');
    expect(instances.first.loader, GameLoader.neoforge);
    expect(instances.first.loaderVersion, GameLoader.neoforge.defaultVersion);
    expect(provider.read(selectedInstanceIdProvider), instances.first.id);
  });

  test('ignores an empty instance name', () async {
    final provider = container();

    final before = (await provider.read(instancesProvider.future)).length;
    await expectLater(
      provider
          .read(instancesProvider.notifier)
          .create(
            name: '   ',
            gameVersion: '1.21.4',
            loader: GameLoader.vanilla,
          ),
      throwsArgumentError,
    );

    expect((await provider.read(instancesProvider.future)).length, before);
  });

  test(
    'deleting the selected instance falls back to the first remaining',
    () async {
      final provider = container();

      final first = (await provider.read(instancesProvider.future)).first;
      provider.read(selectedInstanceIdProvider.notifier).select(first.id);

      await provider.read(instancesProvider.notifier).delete(first.id);

      final remaining = await provider.read(instancesProvider.future);
      expect(remaining, isNotEmpty);
      expect(provider.read(selectedInstanceIdProvider), remaining.first.id);
    },
  );

  test('refresh keeps the local list when the core is unavailable', () async {
    final provider = container();
    final before = await provider.read(instancesProvider.future);

    await provider.read(instancesProvider.notifier).refresh();

    expect(identical(provider.read(instancesProvider).value, before), isTrue);
  });
}
