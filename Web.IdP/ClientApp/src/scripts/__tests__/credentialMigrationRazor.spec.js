import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const markup = readFileSync(resolve(process.cwd(), '../Pages/Account/CredentialMigration.cshtml'), 'utf8')

describe('CredentialMigration Razor proof UI', () => {
  it('gates password entry behind AwaitingNewPassword and exposes server proof actions', () => {
    expect(markup).toContain('if (Model.AwaitingNewPassword)')
    expect(markup).toContain('else if (Model.AwaitingProof)')
    expect(markup.indexOf('asp-page-handler="Commit"')).toBeLessThan(markup.indexOf('else if (Model.AwaitingProof)'))
    expect(markup).toContain('asp-page-handler="VerifyOtp"')
    expect(markup).toContain('asp-page-handler="ResendOtp"')
    expect(markup).toContain('asp-page-handler="VerifyRecoveryEmail"')
    expect(markup).toContain('asp-page-handler="CheckApproval"')
  })

  it('never accepts a recovery address on the pre-authentication migration page', () => {
    expect(markup).not.toContain('CandidateAddress')
    expect(markup).not.toContain('type="email"')
    expect(markup).not.toContain('name="RecoveryEmail"')
  })
})
