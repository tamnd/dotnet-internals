| Coded index | Tag bits | Tags assigned | Two byte index while no target table is longer than |
|---|---|---|---|
| CustomAttributeType | 3 | 2 of 8 | 8,191 rows |
| HasConstant | 2 | 3 of 4 | 16,383 rows |
| HasCustomAttribute | 5 | 22 of 32 | 2,047 rows |
| HasCustomDebugInformation | 5 | 27 of 32 | 2,047 rows |
| HasDeclSecurity | 2 | 3 of 4 | 16,383 rows |
| HasFieldMarshal | 1 | 2 of 2 | 32,767 rows |
| HasSemantics | 1 | 2 of 2 | 32,767 rows |
| Implementation | 2 | 3 of 4 | 16,383 rows |
| MemberForwarded | 1 | 2 of 2 | 32,767 rows |
| MemberRefParent | 3 | 5 of 8 | 8,191 rows |
| MethodDefOrRef | 1 | 2 of 2 | 32,767 rows |
| ResolutionScope | 2 | 4 of 4 | 16,383 rows |
| TypeDefOrRef | 2 | 3 of 4 | 16,383 rows |
| TypeOrMethodDef | 1 | 2 of 2 | 32,767 rows |

#### CustomAttributeType

| Tag | Table |
|---|---|
| 2 | MethodDef |
| 3 | MemberRef |

Tags 0, 1 and 4 through 7 of the 8 are not assigned, and a column of this kind carrying one of them is malformed.

#### HasConstant

| Tag | Table |
|---|---|
| 0 | Field |
| 1 | Param |
| 2 | Property |

Tag 3 of the 4 is not assigned, and a column of this kind carrying it is malformed.

#### HasCustomAttribute

| Tag | Table |
|---|---|
| 0 | MethodDef |
| 1 | Field |
| 2 | TypeRef |
| 3 | TypeDef |
| 4 | Param |
| 5 | InterfaceImpl |
| 6 | MemberRef |
| 7 | Module |
| 8 | DeclSecurity |
| 9 | Property |
| 10 | Event |
| 11 | StandAloneSig |
| 12 | ModuleRef |
| 13 | TypeSpec |
| 14 | Assembly |
| 15 | AssemblyRef |
| 16 | File |
| 17 | ExportedType |
| 18 | ManifestResource |
| 19 | GenericParam |
| 20 | GenericParamConstraint |
| 21 | MethodSpec |

Tags 22 through 31 of the 32 are not assigned, and a column of this kind carrying one of them is malformed.

#### HasCustomDebugInformation

| Tag | Table |
|---|---|
| 0 | MethodDef |
| 1 | Field |
| 2 | TypeRef |
| 3 | TypeDef |
| 4 | Param |
| 5 | InterfaceImpl |
| 6 | MemberRef |
| 7 | Module |
| 8 | DeclSecurity |
| 9 | Property |
| 10 | Event |
| 11 | StandAloneSig |
| 12 | ModuleRef |
| 13 | TypeSpec |
| 14 | Assembly |
| 15 | AssemblyRef |
| 16 | File |
| 17 | ExportedType |
| 18 | ManifestResource |
| 19 | GenericParam |
| 20 | GenericParamConstraint |
| 21 | MethodSpec |
| 22 | Document |
| 23 | LocalScope |
| 24 | LocalVariable |
| 25 | LocalConstant |
| 26 | ImportScope |

Tags 27 through 31 of the 32 are not assigned, and a column of this kind carrying one of them is malformed.

#### HasDeclSecurity

| Tag | Table |
|---|---|
| 0 | TypeDef |
| 1 | MethodDef |
| 2 | Assembly |

Tag 3 of the 4 is not assigned, and a column of this kind carrying it is malformed.

#### HasFieldMarshal

| Tag | Table |
|---|---|
| 0 | Field |
| 1 | Param |

Every one of the 2 tag values is assigned, so no tag in this column is malformed on its own.

#### HasSemantics

| Tag | Table |
|---|---|
| 0 | Event |
| 1 | Property |

Every one of the 2 tag values is assigned, so no tag in this column is malformed on its own.

#### Implementation

| Tag | Table |
|---|---|
| 0 | File |
| 1 | AssemblyRef |
| 2 | ExportedType |

Tag 3 of the 4 is not assigned, and a column of this kind carrying it is malformed.

#### MemberForwarded

| Tag | Table |
|---|---|
| 0 | Field |
| 1 | MethodDef |

Every one of the 2 tag values is assigned, so no tag in this column is malformed on its own.

#### MemberRefParent

| Tag | Table |
|---|---|
| 0 | TypeDef |
| 1 | TypeRef |
| 2 | ModuleRef |
| 3 | MethodDef |
| 4 | TypeSpec |

Tags 5 through 7 of the 8 are not assigned, and a column of this kind carrying one of them is malformed.

#### MethodDefOrRef

| Tag | Table |
|---|---|
| 0 | MethodDef |
| 1 | MemberRef |

Every one of the 2 tag values is assigned, so no tag in this column is malformed on its own.

#### ResolutionScope

| Tag | Table |
|---|---|
| 0 | Module |
| 1 | ModuleRef |
| 2 | AssemblyRef |
| 3 | TypeRef |

Every one of the 4 tag values is assigned, so no tag in this column is malformed on its own.

#### TypeDefOrRef

| Tag | Table |
|---|---|
| 0 | TypeDef |
| 1 | TypeRef |
| 2 | TypeSpec |

Tag 3 of the 4 is not assigned, and a column of this kind carrying it is malformed.

#### TypeOrMethodDef

| Tag | Table |
|---|---|
| 0 | TypeDef |
| 1 | MethodDef |

Every one of the 2 tag values is assigned, so no tag in this column is malformed on its own.
