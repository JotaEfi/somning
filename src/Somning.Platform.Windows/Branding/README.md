# Identidade Somning

Escolha do dono em 2026-10-29, concurso de logo rodada 2 (Codex):

- **02 / S onda** Ã© o logo oficial do Somning 1.0. A faixa de lÃ­quido grafite forma um S dentro da esfera de vidro. `Sources/s-onda-original.png` preserva a imagem original; `Sources/s-onda-512.png` Ã© a exportaÃ§Ã£o quadrada aprovada e usada pelo aplicativo.
- **01 / S fluido** Ã© a alternativa preservada em `Future/` para eventual rebranding. NÃ£o Ã© usada pelo aplicativo.

As imagens foram geradas com imagegen a partir da referÃªncia fornecida da gota do aplicativo. O refinamento buscou o vidro grafite translÃºcido, rim prata, reflexo superior esquerdo e ponto de luz inferior direito; no Onda, a faixa forma um S largo com pontas alongadas e espaÃ§os abertos para leitura pequena.

Execute `powershell -NoProfile -File src/Somning.Platform.Windows/Branding/Generate-Brand.ps1` na raiz do repositÃ³rio para regenerar os recursos. O PNG oficial preserva os bytes da exportaÃ§Ã£o aprovada; o ICO inclui 16, 24, 32, 48, 64, 128 e 256 px, todos com transparÃªncia. O SVG contÃ©m o PNG embutido: Ã© um contÃªiner da arte raster aprovada, nÃ£o um redesenho vetorial.

`SettingsView.xaml` usa o recurso `BrandLogo` apontando para o PNG. `Somning.App.csproj` incorpora o ICO no executÃ¡vel; a bandeja extrai esse Ã­cone e os atalhos criados por `tools/install.ps1` apontam para o Ã­cone do executÃ¡vel.

