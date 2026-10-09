import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

beforeAll(() => window.eval(readFileSync(resolve(process.cwd(), '..', 'wwwroot', 'js', 'account-form-guard.js'), 'utf8')));
beforeEach(() => { document.body.innerHTML = '<form data-account-form><button type="submit">Sign in</button></form>'; });

it('locks submission before module startup and prevents repeated clicks or Enter', () => {
  const form = document.querySelector('form');
  const button = form.querySelector('button');
  const first = new SubmitEvent('submit', {bubbles:true, cancelable:true, submitter:button});
  form.dispatchEvent(first);
  expect(first.defaultPrevented).toBe(false);
  expect(button.disabled).toBe(true);
  const duplicate = new SubmitEvent('submit', {bubbles:true, cancelable:true});
  form.dispatchEvent(duplicate);
  expect(duplicate.defaultPrevented).toBe(true);
});

it('does not submit a verification action that is still disabled', () => {
  const form = document.querySelector('form');
  form.querySelector('button').disabled = true;
  const event = new SubmitEvent('submit', {bubbles:true, cancelable:true});
  form.dispatchEvent(event);
  expect(event.defaultPrevented).toBe(true);
  expect(form.dataset.submitting).toBeUndefined();
});

it('unlocks forms when returning through the browser back cache', () => {
  const form = document.querySelector('form');
  const button = form.querySelector('button');
  form.dispatchEvent(new SubmitEvent('submit', {bubbles:true, cancelable:true, submitter:button}));
  const event = new Event('pageshow');
  Object.defineProperty(event, 'persisted', {value:true});
  window.dispatchEvent(event);
  expect(button.disabled).toBe(false);
  expect(form.dataset.submitting).toBeUndefined();
});
