/**
 * Shared export utilities for downloading files and exporting data
 */
import ExcelJS from 'exceljs'

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
export const exportToExcel = async (headers, rows, filename = 'export.xlsx', sheetName = 'Sheet1') => {
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet(sheetName)
  
  // Add header row with styling
  worksheet.addRow(headers)
  worksheet.getRow(1).font = { bold: true }
  
  // Add data rows
  rows.forEach(row => {
    worksheet.addRow(row)
  })
  
  // Auto-size columns based on content
  worksheet.columns.forEach((column, i) => {
    const maxLength = Math.max(
      headers[i]?.length || 0,
      ...rows.map(row => (row[i] || '').toString().length)
    )
    column.width = Math.min(maxLength + 2, 50) // Cap at 50 characters
  })
  
  // Generate buffer and download
  const buffer = await workbook.xlsx.writeBuffer()
  const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

