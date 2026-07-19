<a name="0.1.4"></a>
## [0.1.4](https://www.github.com/FrostyTwilight/SteamShare/releases/tag/v0.1.4) (2026-07-20)

### 🔧 Build & CI

* fix release changelog step — remove broken versionize regenerate ([df1b006](https://www.github.com/FrostyTwilight/SteamShare/commit/df1b006f52a8d3f0d90b522f2b50ea2a829bc4fd))

<a name="0.1.3"></a>
## [0.1.3](https://www.github.com/FrostyTwilight/SteamShare/releases/tag/v0.1.3) (2026-07-20)

### 🏠 Maintenance

* add dotnet-tools manifest for versionize ([03294c3](https://www.github.com/FrostyTwilight/SteamShare/commit/03294c31a1a79820594d5d87caaf2ef8c5327b16))

<a name="0.1.2"></a>
## [0.1.2](https://www.github.com/FrostyTwilight/SteamShare/releases/tag/v0.1.2) (2026-07-20)

### 🏠 Maintenance

* add .config/ to gitignore and sync solution items ([531df76](https://www.github.com/FrostyTwilight/SteamShare/commit/531df7679bbfb259d7fd42bf4dd0397fb8495e9b))
* fix changelog formatting — remove stale template and dedup sections ([ff93209](https://www.github.com/FrostyTwilight/SteamShare/commit/ff93209ee74c7d10085dda88dfff1a073ebe041e))

<a name="0.1.1"></a>
## [0.1.1](https://www.github.com/FrostyTwilight/SteamShare/releases/tag/v0.1.1) (2026-07-19)

### 🐛 Fixes

* copy README and LICENSE into all release packages ([50592e6](https://www.github.com/FrostyTwilight/SteamShare/commit/50592e6506d49c8b5fb3aa78013c708d6164dd09))
* handle AesGcm tag verification failure on Linux ([7270f19](https://www.github.com/FrostyTwilight/SteamShare/commit/7270f19f76e684fe132258f9252f54cd320f2a96))
* **core:** reorder LZ4 compression before encryption in share key crypto ([b3cacf7](https://www.github.com/FrostyTwilight/SteamShare/commit/b3cacf7f37e81e8333ea6da3c68695285996cbb5))

### 🔧 Build & CI

* add release job with changelog generation, artifact attestation, and trusted build ([bc1a12b](https://www.github.com/FrostyTwilight/SteamShare/commit/bc1a12b52eac92c1bc3348ca14d88d4c4b3bff12))
* conventional commit changelog with categorized sections and commit count ([99c066a](https://www.github.com/FrostyTwilight/SteamShare/commit/99c066a379dff1203ecb8d4007dd031025d6ca80))
* convert Zip release artifacts step to pwsh ([f3f0378](https://www.github.com/FrostyTwilight/SteamShare/commit/f3f037873b76b056416fff6c29176662d3252557))
* integrate Nuke build system with Clean, Restore, Compile, Test, Pack, Format targets ([4e644a7](https://www.github.com/FrostyTwilight/SteamShare/commit/4e644a743d35a20b9a3b131727821ac84c803ec8))
* migrate to Nuke build system (compile/test/format/pack) ([d463883](https://www.github.com/FrostyTwilight/SteamShare/commit/d4638837e0def7440e492a4cc22f9d487564d02a))
* remove publish/ directory, add Nuke bootstrap scripts ([d17f45f](https://www.github.com/FrostyTwilight/SteamShare/commit/d17f45fd9141d630c29f4b578aa0b882a99988f2))
* run test on both ubuntu and windows, fix to use Test target ([caa33ea](https://www.github.com/FrostyTwilight/SteamShare/commit/caa33eac23e759e389e3518f89cec563e88bef58))
* unify release job to pwsh shell ([6c2b3d1](https://www.github.com/FrostyTwilight/SteamShare/commit/6c2b3d17cf0cc3889cb381b800115d42397e4c1f))
* update all GitHub Actions to latest versions ([b1234b1](https://www.github.com/FrostyTwilight/SteamShare/commit/b1234b10413662586d26e88a41fe2693feb18231))
* update dependencies ([51736d4](https://www.github.com/FrostyTwilight/SteamShare/commit/51736d466706c637d7cf60fcf2cc427d819348a1))
* update xunit.v3 2.0.1 → 3.2.2 ([66e77ef](https://www.github.com/FrostyTwilight/SteamShare/commit/66e77ef61ff2ae3834aeb2bfd054ec61ff48b92c))
* use Versionize dotnet tool for conventional commit changelog generation ([a48cfd1](https://www.github.com/FrostyTwilight/SteamShare/commit/a48cfd1106de53932eb5188abc44717a8edd29a7))
* zip per variant, changelog with download table, provenance attestation ([8f35fc8](https://www.github.com/FrostyTwilight/SteamShare/commit/8f35fc81f6123bef068b9a73fb26fb64914108f8))

### ✅ Tests

* migrate xunit v2→v3 and NSubstitute 5→6 ([dffcb8a](https://www.github.com/FrostyTwilight/SteamShare/commit/dffcb8a1192dac7559ceecb98c1ea38e21039698))
* remove UI smoke tests and Avalonia.Headless dependency ([bf3c524](https://www.github.com/FrostyTwilight/SteamShare/commit/bf3c524f4cd37ed7451780804fd5228c57dda5f0))
* update exception type assertion after share key crypto reorder ([3366b41](https://www.github.com/FrostyTwilight/SteamShare/commit/3366b4144eabfbc870e8fa0cf7497724d16a77bb))

### 📖 Documentation

* explain release artifacts with variant comparison table ([80057bc](https://www.github.com/FrostyTwilight/SteamShare/commit/80057bc8ad680202c80e1f10b01305b87b2a5da8))
* simplify release artifacts section — remove reasons and how-to-choose ([f830487](https://www.github.com/FrostyTwilight/SteamShare/commit/f8304878c1b7f080b60c75903117b469ca47af28))
* update repo URL to FrostyTwilight ([d54c6b2](https://www.github.com/FrostyTwilight/SteamShare/commit/d54c6b25896e1f7ea35cf791fe8e8b6bc81f3186))

### 🏠 Maintenance

* enforce LF line endings via .editorconfig and .gitattributes ([51eaeb4](https://www.github.com/FrostyTwilight/SteamShare/commit/51eaeb42212ade6280db6efb707f3ee7abc3f104))
* fix dotnet format violations and unify CI shell to pwsh ([0a5fe55](https://www.github.com/FrostyTwilight/SteamShare/commit/0a5fe559f6f5d1b8c7a1f531d23f4fed38c0a351))
* sync Nuke build schema and project config ([32b81eb](https://www.github.com/FrostyTwilight/SteamShare/commit/32b81eb04e9098554c27af9e3aebaae710e3d273))
* sync solution file with VS changes ([c01e9a0](https://www.github.com/FrostyTwilight/SteamShare/commit/c01e9a05e2998968610a497b6568b0ed01dccaf1))

