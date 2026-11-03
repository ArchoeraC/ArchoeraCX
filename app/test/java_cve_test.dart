import 'package:archoera_cx/services/core/java/java_distribution.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses a CVE report with advisories and aliases', () {
    final report = JavaCveReport.tryParse(<String, Object?>{
      'provider': 'adoptium',
      'version': '21.0.1+9',
      'status': 'ok',
      'reason': null,
      'sources': <String>['nvd', 'osv'],
      'checkedAt': '2026-10-31T03:00:00Z',
      'advisories': <Object?>[
        <String, Object?>{
          'id': 'CVE-2024-21131',
          'severity': 'high',
          'score': 7.5,
          'summary': 'Vulnerability in Oracle Java SE.',
          'url': 'https://nvd.nist.gov/vuln/detail/CVE-2024-21131',
          'publishedAt': '2024-04-16T22:15:42.477',
          'aliases': <String>['GHSA-aaaa-bbbb-cccc'],
        },
      ],
    });

    expect(report, isNotNull);
    expect(report!.available, isTrue);
    expect(report.provider, 'adoptium');
    expect(report.version, '21.0.1+9');
    expect(report.advisories, hasLength(1));
    expect(report.advisories.first.id, 'CVE-2024-21131');
    expect(report.advisories.first.severity, 'high');
    expect(report.advisories.first.score, 7.5);
    expect(report.advisories.first.aliases, <String>['GHSA-aaaa-bbbb-cccc']);
    expect(report.sources, <String>['nvd', 'osv']);
    expect(report.checkedAt, isNotNull);
  });

  test('keeps unavailable reports non-blocking and empty', () {
    final report = JavaCveReport.tryParse(<String, Object?>{
      'provider': 'zulu',
      'version': '21.0.6',
      'status': 'unavailable',
      'reason': 'JAVA_CVE_RATE_LIMITED',
      'sources': <String>[],
      'advisories': <Object?>[],
    });

    expect(report, isNotNull);
    expect(report!.available, isFalse);
    expect(report.reason, 'JAVA_CVE_RATE_LIMITED');
    expect(report.advisories, isEmpty);
  });

  test('rejects a report without provider or version', () {
    expect(JavaCveReport.tryParse(<String, Object?>{'status': 'ok'}), isNull);
  });

  test('parses target platform triples and the current-device flag', () {
    final host = JavaDistributionPlatformInfo.tryParse(<String, Object?>{
      'os': 'linux',
      'arch': 'x64',
      'archive': 'tar.gz',
      'current': true,
    });
    final foreign = JavaDistributionPlatformInfo.tryParse(<String, Object?>{
      'os': 'windows',
      'arch': 'aarch64',
      'archive': 'zip',
      'current': false,
    });

    expect(host, isNotNull);
    expect(host!.current, isTrue);
    expect(host.os, 'linux');
    expect(host.arch, 'x64');
    expect(host.archive, 'tar.gz');
    expect(foreign, isNotNull);
    expect(foreign!.current, isFalse);
    expect(foreign.os, 'windows');
    expect(foreign.arch, 'aarch64');
    expect(foreign.archive, 'zip');
    expect(
      JavaDistributionPlatformInfo.tryParse(<String, Object?>{'os': 'linux'}),
      isNull,
    );
  });
}
