# Schema.CLI Documentation

The `Schema.CLI` is a command-line tool designed for managing and manipulating JSON-LD schema collections. This documentation provides an overview of all available commands, their parameters, and usage examples.

---

## Commands Overview

### 1. `init`
**Description**: Initializes the schema at a specified path and creates a `.env` file.

**Parameters**:
- `--path` (required): The path where the schema will be initialized.

**Example**:
```bash
schema init --path ./SeedSchema
```

---

### 2. `clone`
**Description**: Clones the latest version of the schema.

**Example**:
```bash
schema clone
```

---

### 3. `add`
**Description**: Adds various schema items. This command has subcommands for adding specific items.

#### Subcommands:
- **`class`**: Adds a class.
  - `--term` (required): The term for the class.
  - `--uri` (optional): The URI for the class.

  **Example**:
  ```bash
  schema add class --term ceterms:TestClass
  ```

- **`property`**: Adds a property.
  - `--term` (required): The term for the property.
  - `--uri` (optional): The URI for the property.

  **Example**:
  ```bash
  schema add property --term ceterms:TestProperty
  ```

- **`concept`**: Adds a concept.
  - `--term` (required): The term for the concept.
  - `--uri` (optional): The URI for the concept.

  **Example**:
  ```bash
  schema add concept --term ceterms:TestConcept
  ```

- **`conceptscheme`**: Adds a concept scheme.
  - `--term` (required): The term for the concept scheme.
  - `--uri` (optional): The URI for the concept scheme.

  **Example**:
  ```bash
  schema add conceptscheme --term ceterms:TestConceptScheme
  ```

- **`context`**: Adds a context.
  - `--term` (required): The term for the context.
  - `--uri` (optional): The URI for the context.
  - `--field` (optional): The field to add.
  - `--object` (optional): The object to add.

  **Example**:
  ```bash
  schema add context --term ceterms:name --field @@container --object @@language
  ```

- **`triple`**: Adds a subject-predicate-object triple.
  - `--subject` (required): The triple subject.
  - `--predicate` (required): The triple predicate.
  - `--object` (required): The object to add.

  **Example**:
  ```bash
  schema add triple --subject ceterms:TestProperty --predicate schema:domainIncludes --object ceterms:TestClass
  ```

---

### 4. `update`
**Description**: Updates schema items. This command has subcommands for updating specific items.

#### Subcommands:
- **`context`**: Updates a context.
  - `--term` (required): The term for the context.
  - `--uri` (optional): The URI for the context.
  - `--field` (optional): The field to update.
  - `--object` (optional): The object to update.

  **Example**:
  ```bash
  schema update context --term ceterms:name --field @@type --object xsd:string
  ```

---

### 5. `remove`
**Description**: Removes various schema items. This command has subcommands for removing specific items.

#### Subcommands:
- **`class`**: Removes a class.
  - `--term` (required): The term for the class.

  **Example**:
  ```bash
  schema remove class --term ceterms:TestClass
  ```

- **`field`**: Removes a field.
  - `--subject` (required): The subject of the field.
  - `--predicate` (required): The predicate of the field.

  **Example**:
  ```bash
  schema remove field --subject ceterms:TestClass --predicate vs:term_status
  ```

- **`concept`**: Removes a concept.
  - `--term` (required): The term for the concept.

  **Example**:
  ```bash
  schema remove concept --term ceterms:TestConcept
  ```

- **`conceptscheme`**: Removes a concept scheme.
  - `--term` (required): The term for the concept scheme.

  **Example**:
  ```bash
  schema remove conceptscheme --term ceterms:TestConceptScheme
  ```

- **`context`**: Removes a context.
  - `--term` (required): The term for the context.
  - `--field` (optional): The field to remove.

  **Example**:
  ```bash
  schema remove context --term ceterms:name --field @container
  ```

- **`triple`**: Removes a subject-predicate-object triple.
  - `--subject` (required): The triple subject.
  - `--predicate` (required): The triple predicate.
  - `--object` (required): The object to remove.

  **Example**:
  ```bash
  schema remove triple --subject ceterms:TestProperty --predicate schema:rangeIncludes --object xsd:string
  ```

---

### 6. `set`
**Description**: Sets values for schema items. This command has subcommands for setting specific values.

#### Subcommands:
- **`field`**: Sets a field.
  - `--subject` (required): The subject of the field.
  - `--predicate` (required): The predicate of the field.
  - `--object` (required): The object to set.

  **Example**:
  ```bash
  schema set triple --subject ceterms:TestClass --predicate vs:term_status --object stable
  ```

- **`language-property`**: Sets a language property.
  - `--subject` (required): The subject of the property.
  - `--predicate` (required): The predicate of the property.
  - `--language` (required): The language of the property.
  - `--object` (required): The object to set.

  **Example**:
  ```bash
  schema set language-property --subject ceterms:TestClass --predicate rdfs:label --language en --object "Test Class"
  ```

---

### 7. `validate`
**Description**: Validates the schema using SHACL shapes.

**Parameters**:
- `--shapes` (required): The path to the SHACL shapes file.

**Example**:
```bash
schema validate --shapes ./shapes.ttl
```

---

## Notes
- Ensure the `.env` file is properly configured with the `SCHEMA_ORIGINAL` environment variable after running `init`.
- Use the `test-script.txt` as a reference for additional examples and workflows.


## Split and merge schema files

After `schema init` selects `ctdl`, `ctdlasn`, or `qdata`, use these commands to synchronize the two schema representations:

```text
schema split
schema merge
```

`split` replaces the selected schema's `Split` directory from `Merged/<schema>-schema.jsonld` and writes `_meta.json` to preserve graph ordering and root context. `merge` rebuilds `Merged/<schema>-schema.jsonld` from the selected schema's `Split` directory. The commands use the schema-specific filenames for CTDL, CTDL-ASN, and QData.

## SPARQL Update

SPARQL Update operates directly on the RDF graph parsed from the selected JSON-LD schema. Turtle conversion is not required.

```powershell
schema.cli.exe init --path "D:\Schema-Development\src\Schema" --schema ctdlasn
schema.cli.exe sparql --file "D:\updates\ctdlasn-update.rq"
```

An inline update may also be supplied:

```powershell
schema.cli.exe sparql --update "INSERT DATA { <https://example.org/Thing> <http://www.w3.org/2000/01/rdf-schema#label> \"Thing\"@en . }"
```

## Turtle conversion

Export the selected JSON-LD schema to Turtle:

```powershell
schema.cli.exe rdf export-turtle --output "D:\exports\ctdlasn.ttl"
```

Import Turtle as the selected schema's new RDF graph:

```powershell
schema.cli.exe rdf import-turtle --input "D:\exports\ctdlasn.ttl"
```

JSON-LD and Turtle are different RDF serializations. Conversion preserves RDF meaning (including datatypes, language tags, IRIs, and blank-node graph structure), but it cannot preserve JSON-specific formatting, object ordering, array ordering, or the exact original JSON-LD compaction.
