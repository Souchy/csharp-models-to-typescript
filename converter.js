
const fs = require('fs');
const path = require('path');
const camelcase = require('camelcase');
const dedent = require('dedent');
const endent = require('endent');

const flatten = arr => arr.reduce((a, b) => a.concat(b), []);

const arrayRegex = /^(.+)\[\]$/;
const simpleCollectionRegex = /^(?:I?List|IReadOnlyList|IEnumerable|ICollection|IReadOnlyCollection|HashSet)<([\w\d]+)>\??$/;
const collectionRegex = /^(?:I?List|IReadOnlyList|IEnumerable|ICollection|IReadOnlyCollection|HashSet)<(.+)>\??$/;
const simpleDictionaryRegex = /^(?:I?Dictionary|SortedDictionary|IReadOnlyDictionary)<([\w\d]+)\s*,\s*([\w\d]+)>\??$/;
const dictionaryRegex = /^(?:I?Dictionary|SortedDictionary|IReadOnlyDictionary)<([\w\d]+)\s*,\s*(.+)>\??$/;

const defaultTypeTranslations = {
    int: 'number',
    double: 'number',
    float: 'number',
    Int32: 'number',
    Int64: 'number',
    short: 'number',
    long: 'number',
    decimal: 'number',
    byte: 'number',
    bool: 'boolean',
    DateTime: 'string',
    DateTimeOffset: 'string',
    Guid: 'string',
    dynamic: 'any',
    object: 'any',
};

const createConverter = config => {
    const typeTranslations = Object.assign({}, defaultTypeTranslations, config.customTypeTranslations);

    const convert = json => {
        const content = json.map(file => {
            const filename = path.relative(process.cwd(), file.FileName);

            let exportPath = config.output + "/" + file.FileName;
            exportPath = exportPath.replace(config.root, "").replace(".cs", ".ts");

            const rows = flatten([
                ...file.Models.map(model => convertModel(model, filename)),
                ...file.Enums.map(enum_ => convertEnum(enum_, filename)),
                ...file.Controllers.map(cont_ => convertController(cont_, filename)),
            ]);

            let text = rows
                .map(row => config.namespace ? `    ${row}` : row)
                // .map(row => `    ${row}`)
                .join('\n');

            if (true) {
                ensureDirectoryExistence(exportPath);
                let namespace = config.namespace + '.' + file.Namespace;
                let imports = convertImports(file.FileName, file.Imports);
                if (imports) imports += '\n\n';
                // console.log(`imports: [${imports}]`);

                fs.writeFile(exportPath,
                    imports + (config.namespace ? `namespace ${namespace} {\n${text}}` : text),
                    err => {
                        if (err) return console.error(err);
                    });
            }
            return "";
        });

        const filteredContent = content.filter(x => x.length > 0);

        if (config.namespace) {
            return [
                `declare module ${config.namespace} {`,
                ...filteredContent,
                '}',
            ].join('\n');
        } else {
            return filteredContent.join('\n');
        }
    };

    function convertImports(filename, imports) {
        let travelUp = filename.replace(config.root, "");
        if (travelUp[0] == "/") travelUp = travelUp.substring(1);
        let count = travelUp.split("/").length - 1;
        travelUp = Array.from("../".repeat(count)).join("");
        imports = imports
            .map(i => {
                if (!i) return "";
                let importName = path.basename(i).replace(".cs", "");
                let importPath = i.replace(config.root, "").replace(".cs", "");
                if (importPath[0] == "/") importPath = importPath.substring(1);
                return `import { ${importName} } from './${travelUp}${importPath}';`
            })
            .filter(i => i != "");
        imports.unshift(`import { Environment } from './${travelUp}environment';`);
        return imports.join("\n");
    }

    function ensureDirectoryExistence(filePath) {
        var dirname = path.dirname(filePath);
        if (fs.existsSync(dirname)) {
            return true;
        }
        ensureDirectoryExistence(dirname);
        fs.mkdirSync(dirname);
    }

    const convertModel = (model, filename) => {
        const rows = [];

        if (model.BaseClasses) {
            model.IndexSignature = model.BaseClasses.find(type => type.match(dictionaryRegex));
            model.BaseClasses = model.BaseClasses.filter(type => !type.match(dictionaryRegex));
            model.BaseClasses = model.BaseClasses.filter(type => !config.omitBaseClasses.includes(type));
        }

        const members = [...(model.Fields || []), ...(model.Properties || [])];
        const baseClasses = model.BaseClasses && model.BaseClasses.length ? ` extends ${model.BaseClasses.join(', ')}` : '';

        if (!config.omitFilePathComment) {
            rows.push(`// ${filename}`);
        }
        rows.push(`export class ${model.ModelName}${baseClasses} {`);

        const propertySemicolon = config.omitSemicolon ? '' : ';';

        if (model.IndexSignature) {
            rows.push(`    ${convertIndexType(model.IndexSignature)}${propertySemicolon}`);
        }

        members.forEach(member => {
            rows.push(`    ${convertProperty(member)}${propertySemicolon}`);
        });

        rows.push(`}\n`);

        return rows;
    };

    const convertController = (model, filename) => {
        const rows = [];

        if (model.BaseClasses) {
            model.IndexSignature = model.BaseClasses.find(type => type.match(dictionaryRegex));
            model.BaseClasses = model.BaseClasses.filter(type => !type.match(dictionaryRegex));
            model.BaseClasses = model.BaseClasses.filter(type => !config.omitBaseClasses.includes(type));
        }

        const members = [...(model.Methods || [])];
        const baseClasses = model.BaseClasses && model.BaseClasses.length ? ` extends ${model.BaseClasses.join(', ')}` : '';

        if (!config.omitFilePathComment) {
            rows.push(`// ${filename}`);
        }
        rows.push(`export class ${model.ModelName}${baseClasses} {`);

        const propertySemicolon = config.omitSemicolon ? '' : ';';

        if (model.IndexSignature) {
            rows.push(`    ${convertIndexType(model.IndexSignature)}${propertySemicolon}`);
        }

        members.forEach(member => {
            rows.push(`    ${convertMethod(member)}`);
        });

        rows.push(`}\n`);

        return rows;
    };

    const convertEnum = (enum_, filename) => {
        const rows = [];
        if (!config.omitFilePathComment) {
            rows.push(`// ${filename}`);
        }

        const entries = Object.entries(enum_.Values);

        const getEnumStringValue = (value) => config.camelCaseEnums
            ? camelcase(value)
            : value;

        const lastValueSemicolon = config.omitSemicolon ? '' : ';';

        if (config.stringLiteralTypesInsteadOfEnums) {
            rows.push(`export type ${enum_.Identifier} =`);

            entries.forEach(([key], i) => {
                const delimiter = (i === entries.length - 1) ? lastValueSemicolon : ' |';
                rows.push(`    '${getEnumStringValue(key)}'${delimiter}`);
            });

            rows.push('');
        } else {
            rows.push(`export enum ${enum_.Identifier} {`);

            entries.forEach(([key, value], i) => {
                if (config.numericEnums) {
                    rows.push(`    ${key} = ${value != null ? value : i},`);
                } else {
                    rows.push(`    ${key} = '${getEnumStringValue(key)}',`);
                }
            });

            rows.push(`}\n`);
        }

        return rows;
    };

    const convertProperty = property => {
        const optional = property.Type.endsWith('?');
        const identifier = convertIdentifier(optional ? `${property.Identifier.split(' ')[0]}?` : property.Identifier.split(' ')[0]);

        const type = parseType(property.Type);

        return `${identifier}: ${type}`;
    };

    const convertMethod = method => {
        const identifier = convertIdentifier(method.Identifier.split(' ')[0]);
        const type = parseType(method.Type);

        const params = method.Parameters
            .map(p => `${p.Identifier}: ${parseType(p.Type)}`)
            .join(", ");

        let route = convertRoute(method);
        let body = method.Parameters
            .filter(p => !route.includes(`{${p.Identifier}}`))
            .map(p => `${p.Identifier}`)[0];

        let code = '';
        let codeLines = []
        if (method.HttpMethod == "HttpPost") {
            codeLines = dedent(`
                    //${method.HttpMethod} at ${route}
                    return Environment.post(${route}, JSON.stringify(${body ? body : '{}'}));
                `).split("\n");
        } else {
            codeLines = dedent(`
                    //${method.HttpMethod} at ${route}
                    return Environment.get(${route})
                `).split("\n");
        }
        if (config.namespace) {
            code = `\t${codeLines.join("\n\t\t")}\n\t`;
        } else {
            code = `${codeLines.join("\n\t\t")}\n`;
        }
        return `public async ${identifier}(${params}): ${type} {\n\t\t${code}\t}`;
    };

    function convertRoute(method) {
        let route = method.Route;
        if (route != null) {
            route = route.replace('\"', '').replace('\"', '');
            // replace tags that have a default value with just the tag (ex: {duration=500} => {duration})
            let start = -1;
            let end = 0;
            while ((start = route.indexOf('{', end + 1)) != -1) {
                end = route.indexOf('}', start);
                let sub = route.substring(start + 1, end);
                let sub2 = sub.split('=')[0];
                route = route.replace(sub, sub2);
            }
            // add $ to tags to evaluate them
            for (let p of method.Parameters) {
                route = route.replace(`{${p.Identifier}}`, `\${${p.Identifier}}`);
            }
        } else {
            route = method.Identifier;
        }
        return `Environment.url + \`${route}\``;
    }

    const convertIndexType = indexType => {
        const dictionary = indexType.match(dictionaryRegex);
        const simpleDictionary = indexType.match(simpleDictionaryRegex);

        propType = simpleDictionary ? dictionary[2] : parseType(dictionary[2]);

        return `[key: ${convertType(dictionary[1])}]: ${convertType(propType)}`;
    };

    const convertRecord = indexType => {
        const dictionary = indexType.match(dictionaryRegex);
        const simpleDictionary = indexType.match(simpleDictionaryRegex);

        propType = simpleDictionary ? dictionary[2] : parseType(dictionary[2]);

        return `Record<${convertType(dictionary[1])}, ${convertType(propType)}>`;
    };

    const parseType = propType => {
        const array = propType.match(arrayRegex);
        if (array) {
            propType = array[1];
        }

        const collection = propType.match(collectionRegex);
        const dictionary = propType.match(dictionaryRegex);

        let type;

        if (collection) {
            const simpleCollection = propType.match(simpleCollectionRegex);
            propType = simpleCollection ? collection[1] : parseType(collection[1]);
            type = `${convertType(propType)}[]`;
        } else if (dictionary) {
            type = `${convertRecord(propType)}`;
        } else {
            const optional = propType.endsWith('?');
            type = convertType(optional ? propType.slice(0, propType.length - 1) : propType);
        }

        return array ? `${type}[]` : type;
    };

    const convertIdentifier = identifier => config.camelCase ? camelcase(identifier, config.camelCaseOptions) : identifier;
    const convertType = type => type in typeTranslations ? typeTranslations[type] : type;

    return convert;
};

module.exports = createConverter;
