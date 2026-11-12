// Vitest'i büyük harfli sürücüyle başlatır (npm test).
// Windows'ta terminal küçük harfli sürücüyle (c:\, VS Code'un varsayılanı) açıldığında Vitest kendi modülünü c:/
// yolundan, test dosyalarını C:/ yolundan yükler; iki ayrı örnek olur ve testler "Cannot read properties of undefined
// (reading 'config')" ile düşer. Config okunmadan önce olduğu için vite.config.ts'ten düzeltilemiyor.
import { spawnSync } from 'node:child_process'
import { join } from 'node:path'

const cwd = process.cwd().replace(/^[a-z]:/, (drive) => drive.toUpperCase())
// Paketin "exports"u CLI dosyasını açmıyor; bin yolu doğrudan ve o da büyük harfli dizinden (npm test web/ içinde koşar).
const cli = join(cwd, 'node_modules', 'vitest', 'vitest.mjs')
const result = spawnSync(process.execPath, [cli, ...process.argv.slice(2)], { cwd, stdio: 'inherit' })
process.exit(result.status ?? 1)
