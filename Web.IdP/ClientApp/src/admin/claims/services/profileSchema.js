export async function getProfileSchema() {
  const response = await fetch('/api/admin/claims/profile-sources')
  if (!response.ok) throw new Error('Profile schema unavailable')
  return response.json()
}
