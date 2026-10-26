# Third-party notices

HybridAuth IdP remains licensed under the [MIT License](LICENSE).
Third-party components retain their own licenses.

## ITfoxtec SAML integration

- ITfoxtec.Identity.Saml2 4.21.0
- ITfoxtec.Identity.Saml2.MvcCore 4.21.0
- License: BSD-3-Clause; Copyright (c) 2025, ITfoxtec. All rights reserved.
- Source: https://github.com/ITfoxtec/ITfoxtec.Identity.Saml2
- Package repository commit: `aeb3c67c00ca060be1e81feaef3fd756fafd20e1`
- Full copyright notice, redistribution conditions, no-endorsement condition
  and disclaimer: [ITfoxtec license](licenses/ITfoxtec.Identity.Saml2-LICENSE.txt).

Both NuGet packages declare BSD-3-Clause and the same repository commit.
The standalone SAML sample also uses these packages and adapts this repository's
SAML integration code. Retain this notice and the full license when distributing
source or binaries, including a separately distributed sample. Neither ITfoxtec
nor its contributors endorse this project by virtue of its use of the library.

Application publish outputs include `LICENSE`, `THIRD_PARTY_NOTICES.md` and
`licenses/ITfoxtec.Identity.Saml2-LICENSE.txt`. The runtime image copies these
files from the publish output into `/app`.
