import 'package:archoera_cx/services/core/resource_models.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses project gallery and metadata', () {
    final project = ResourceProject.tryParse({
      'id': 'abc',
      'name': 'Project',
      'gallery': [
        {
          'url': 'https://cdn.modrinth.com/data/abc/gallery/one.png',
          'featured': true,
          'title': 'Screenshot',
          'ordering': 0,
        },
      ],
      'sourceUrl': 'https://example.com/source',
      'publishedAt': '2026-01-01T00:00:00Z',
      'categories': ['optimization'],
    });

    expect(project, isNotNull);
    expect(project!.gallery, hasLength(1));
    expect(project.gallery.first.featured, isTrue);
    expect(project.gallery.first.title, 'Screenshot');
    expect(project.sourceUrl, 'https://example.com/source');
  });
}
