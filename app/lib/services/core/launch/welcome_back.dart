import '../../../models/account.dart';
import '../../../models/game_instance.dart';

/// How long every configured instance must have been idle before the
/// "welcome back" easter egg appears on the next launch.
const welcomeBackIdleDuration = Duration(days: 30);

/// Whether every instance has been idle for longer than
/// [welcomeBackIdleDuration] at [now].
///
/// The list must not be empty, and an instance that has never been launched
/// (`lastPlayed == null`) keeps the easter egg quiet: the player has not gone
/// through a full idle period yet.
bool shouldShowWelcomeBack(
  Iterable<GameInstance> instances, {
  required DateTime now,
}) {
  final cutoff = now.subtract(welcomeBackIdleDuration);
  var empty = true;
  for (final instance in instances) {
    empty = false;
    final lastPlayed = instance.lastPlayed;
    if (lastPlayed == null || !lastPlayed.isBefore(cutoff)) return false;
  }
  return !empty;
}

/// The name shown in the easter egg: a Microsoft account wins over the
/// currently selected account, which keeps offline and authlib players covered.
String? welcomeBackAccountName(List<Account> accounts, {Account? current}) {
  if (current?.kind == AccountKind.microsoft) return current!.displayName;
  for (final account in accounts) {
    if (account.kind == AccountKind.microsoft) return account.displayName;
  }
  return current?.displayName;
}
