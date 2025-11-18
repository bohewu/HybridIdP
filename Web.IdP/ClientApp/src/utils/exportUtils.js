/**
 * Shared export utilities for downloading files and exporting data
 */
import * as XLSX from 'xlsx'

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

/**
 * Export data to Excel (.xlsx) format
 * @param {Array<string>} headers - Array of header strings
 * @param {Array<Array<string>>} rows - Array of row arrays
 * @param {string} filename - The filename for download (default: 'export.xlsx')
 * @param {string} sheetName - The worksheet name (default: 'Sheet1')
 */
export const exportToExcel = (headers, rows, filename = 'export.xlsx', sheetName = 'Sheet1') => {
  // Create worksheet from array of arrays
  const wsData = [headers, ...rows]
  const ws = XLSX.utils.aoa_to_sheet(wsData)
  
  // Auto-size columns based on content
  const colWidths = headers.map((header, i) => {
    const maxLength = Math.max(
      header.length,
      ...rows.map(row => (row[i] || '').toString().length)
    )
    return { wch: Math.min(maxLength + 2, 50) } // Cap at 50 characters
  })
  ws['!cols'] = colWidths
  
  // Create workbook and add worksheet
  const wb = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(wb, ws, sheetName)
  
  // Write file
  XLSX.writeFile(wb, filename)
}
