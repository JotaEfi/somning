# Identidade Somning

Escolha do dono em 2026-10-29, concurso de logo rodada 2 (Codex):

- **02 / S onda** é o logo oficial do Somning 1.0. A faixa de líquido grafite forma um S dentro da esfera de vidro. `Sources/s-onda-original.png` preserva a imagem original; `Sources/s-onda-512.png` é a exportação quadrada aprovada e usada pelo aplicativo.
- **01 / S fluido** é a alternativa preservada em `Future/` para eventual rebranding. Não é usada pelo aplicativo.

As imagens foram geradas com imagegen a partir da referência fornecida da gota do aplicativo. O refinamento buscou o vidro grafite translúcido, rim prata, reflexo superior esquerdo e ponto de luz inferior direito; no Onda, a faixa forma um S largo com pontas alongadas e espaços abertos para leitura pequena.

Execute `powershell -NoProfile -File src/Somning.Platform.Windows/Branding/Generate-Brand.ps1` na raiz do repositório para regenerar os recursos. O PNG oficial preserva os bytes da exportação aprovada; o ICO inclui 16, 24, 32, 48, 64, 128 e 256 px, todos com transparência. O SVG contém o PNG embutido: é um contêiner da arte raster aprovada, não um redesenho vetorial.

`SettingsView.xaml` usa o recurso `BrandLogo` apontando para o PNG. `Somning.App.csproj` incorpora o ICO no executável; a bandeja extrai esse ícone e os atalhos criados por `tools/install.ps1` apontam para o ícone do executável.
