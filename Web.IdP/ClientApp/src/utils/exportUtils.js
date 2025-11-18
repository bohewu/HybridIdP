/**
 * Shared export utilities for downloading files and exporting data
 */

/**
 * Download a file with the given content
 * @param {string} content - The file content
 * @param {string} filename - The filename for download
 * @param {string} mimeType - The MIME type of the file
 */
export const downloadFile = (content, filename, mimeType) => {
  const blob = new Blob([content], { type: mimeType })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

/**
 * Export data to CSV format
 * @param {Array<string>} headers - Array of header strings
 * @param {Array<Array<string>>} rows - Array of row arrays
 * @param {string} filename - The filename for download (default: 'export.csv')
 */
export const exportToCsv = (headers, rows, filename = 'export.csv') => {
  const csvContent = [headers, ...rows]
    .map(row => row.map(field => `"${field}"`).join(','))
    .join('\n')
  
  downloadFile(csvContent, filename, 'text/csv')
}
