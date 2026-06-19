// clipboardy's package.json "exports" map has no "types" condition and resolves
// the browser entry to browser.js, which has no co-located declarations. Under
// TypeScript's "bundler" module resolution (which respects "exports"), that
// leaves the import untyped. Declare the small async surface we use until the
// package ships a types export.
declare module 'clipboardy' {
  const clipboard: {
    write(text: string): Promise<void>;
    read(): Promise<string>;
  };
  export default clipboard;
}
