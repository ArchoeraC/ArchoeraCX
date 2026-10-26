/// Result of `platform.singleInstance`.
///
/// [primary] is true for the process that owns the launcher lock. A secondary
/// process gets `false` and should exit after the native side forwards an
/// activation message to the primary. [signaled] records whether that forward
/// reached the primary listener; it does not change the "do not start a second
/// window" decision.
final class SingleInstanceClaim {
  const SingleInstanceClaim({required this.primary, required this.signaled});

  final bool primary;
  final bool signaled;
}
