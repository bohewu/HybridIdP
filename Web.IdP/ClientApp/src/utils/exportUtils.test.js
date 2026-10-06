import { describe, expect, it } from 'vitest'
import { escapeCsvCell } from './exportUtils'

describe('CSV cell export', () => {
  it.each(['=1+1', '+1+1', '-1+1', '@SUM(A1)', '  =1+1', '\t+1', '\r\n@SUM(A1)', '\u0000-1'])('neutralizes formula cell %j', value => {
    expect(escapeCsvCell(value)).toBe(`"'${value}"`)
  })

  it('keeps quotes, commas and line breaks inside one escaped cell', () => {
    expect(escapeCsvCell('client ",=SUM(A1)\r\nnext')).toBe('"client "",=SUM(A1)\r\nnext"')
  })

  it.each([['plain text', '"plain text"'], ['a,b', '"a,b"'], [null, '""'], [0, '"0"']])('preserves ordinary cell %j', (value, expected) => {
    expect(escapeCsvCell(value)).toBe(expected)
  })
})
