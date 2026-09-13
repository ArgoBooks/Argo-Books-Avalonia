# CRA XML schemas, vendored

CRA's schema package for the 2026 filing season, unmodified: `xmlschm1-26-3.zip`,
`SchemasEFV-Published - 202601`, dated 11 December 2025. 109 files, about 900 KB.

The whole package is here rather than the handful of files on the T4 path, because `complex.xsd`
is shared across every return type CRA accepts and includes all of them. Following the imports
from `T619_T4.xsd` reaches 72 of the 109 files, so trimming would mean editing CRA's own schemas,
and a schema you have edited no longer tells you whether CRA will accept the file.

Committed rather than downloaded so `T4XmlSchemaTests` runs offline and deterministically. A test
that fetches a live government URL fails when you have no connection, when CRA moves a page, or
when their site is slow, and none of those are the failure it exists to catch.

## Updating, once a year

CRA publishes the new package in January, and it is listed in `docs/Payroll rate updates.md`
under "What else needs a look each year".

1. Download the current package from CRA's XML specifications page.
2. Delete the `.xsd` files here and drop in the new ones. Do not edit them.
3. Update `SchemaVersion` in `T4XmlSchemaTests` to the `Version#` in the new file headers.
4. Update the specification version named in the `T4XmlWriter` class comment.
5. Run the tests. A schema change that breaks the writer fails here rather than at the deadline.

Steps 3 and 4 are checked against each other, so doing one without the other fails.

Current edition: **1.26**
