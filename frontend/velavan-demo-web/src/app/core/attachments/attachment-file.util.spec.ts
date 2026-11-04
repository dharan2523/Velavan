import {
  formatFileSize,
  parseAttachmentFileName
} from './attachment-file.util';

describe('attachment file utilities', () => {
  it('formats file sizes using binary units', () => {
    expect(formatFileSize(0)).toBe('0 B');
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(2048)).toBe('2.0 KB');
    expect(formatFileSize(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  it('parses quoted and encoded content disposition filenames', () => {
    expect(
      parseAttachmentFileName(
        'attachment; filename="terms.pdf"',
        'fallback.pdf'
      )
    ).toBe('terms.pdf');
    expect(
      parseAttachmentFileName(
        "attachment; filename*=UTF-8''report%20final.pdf",
        'fallback.pdf'
      )
    ).toBe('report final.pdf');
    expect(parseAttachmentFileName(null, 'fallback.pdf')).toBe(
      'fallback.pdf'
    );
    expect(
      parseAttachmentFileName(
        "attachment; filename*=UTF-8''bad%ZZ.pdf",
        'fallback.pdf'
      )
    ).toBe('fallback.pdf');
  });
});
