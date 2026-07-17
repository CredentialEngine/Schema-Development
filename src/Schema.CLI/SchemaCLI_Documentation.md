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
  - `--value` (optional): The value to add.

  **Example**:
  ```bash
  schema add context --term ceterms:name --field @@container --value @@language
  ```

- **`value`**: Adds a value to a field.
  - `--subject` (required): The subject of the value.
  - `--predicate` (required): The predicate of the value.
  - `--value` (required): The value to add.

  **Example**:
  ```bash
  schema add value --subject ceterms:TestProperty --predicate schema:domainIncludes --value ceterms:TestClass
  ```

---

### 4. `update`
**Description**: Updates schema items. This command has subcommands for updating specific items.

#### Subcommands:
- **`context`**: Updates a context.
  - `--term` (required): The term for the context.
  - `--uri` (optional): The URI for the context.
  - `--field` (optional): The field to update.
  - `--value` (optional): The value to update.

  **Example**:
  ```bash
  schema update context --term ceterms:name --field @@type --value xsd:string
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

- **`value`**: Removes a value from a field.
  - `--subject` (required): The subject of the value.
  - `--predicate` (required): The predicate of the value.
  - `--value` (required): The value to remove.

  **Example**:
  ```bash
  schema remove value --subject ceterms:TestProperty --predicate schema:rangeIncludes --value xsd:string
  ```

---

### 6. `set`
**Description**: Sets values for schema items. This command has subcommands for setting specific values.

#### Subcommands:
- **`field`**: Sets a field.
  - `--subject` (required): The subject of the field.
  - `--predicate` (required): The predicate of the field.
  - `--value` (required): The value to set.

  **Example**:
  ```bash
  schema set field --subject ceterms:TestClass --predicate vs:term_status --value stable
  ```

- **`language-property`**: Sets a language property.
  - `--subject` (required): The subject of the property.
  - `--predicate` (required): The predicate of the property.
  - `--language` (required): The language of the property.
  - `--value` (required): The value to set.

  **Example**:
  ```bash
  schema set language-property --subject ceterms:TestClass --predicate rdfs:label --language en --value "Test Class"
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
