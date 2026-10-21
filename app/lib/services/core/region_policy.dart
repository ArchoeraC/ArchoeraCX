/// Coarse region policy used for account restrictions.
final class RegionPolicyState {
  const RegionPolicyState({
    required this.region,
    required this.microsoftOnly,
    required this.source,
    this.countryCode,
  });

  final String region;
  final bool microsoftOnly;
  final String source;
  final String? countryCode;

  static RegionPolicyState? tryParse(Object? value) {
    if (value is! Map) return null;
    final region = value['region']?.toString();
    if (region == null || region.isEmpty) return null;
    return RegionPolicyState(
      region: region,
      microsoftOnly: value['microsoftOnly'] == true,
      source: value['source']?.toString() ?? 'unknown',
      countryCode: value['countryCode']?.toString(),
    );
  }
}
