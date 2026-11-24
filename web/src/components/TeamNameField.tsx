/** Sunucudaki TeamName ile aynı sınır (Domain/TeamName.cs); asıl denetim sunucuda. */
export const TEAM_NAME_MAX = 16

// Takım adı alanı (lobi açma ve katılma formlarında). İsteğe bağlı: boş bırakan "Sen" / "Rakip" olarak görünür.
export function TeamNameField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <label className="team-name-field">
      <span>Takım adın</span>
      <input
        type="text"
        value={value}
        maxLength={TEAM_NAME_MAX}
        placeholder="ör. Kuzey Kurtları"
        autoComplete="off"
        spellCheck={false}
        onChange={(e) => onChange(e.target.value)}
      />
      <small className="muted">İsteğe bağlı · en fazla {TEAM_NAME_MAX} karakter</small>
    </label>
  )
}
