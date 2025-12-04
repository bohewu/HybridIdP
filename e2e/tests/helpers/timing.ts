import { Page, Locator } from '@playwright/test';

/**
 * Wait for a list item to appear with retry logic that polls the API
 * instead of relying on UI refresh timing.
 */
export async function waitForListItemWithRetry(
  page: Page,
  entity: string,
  searchQuery: string,
  options: {
    timeout?: number;
    listSelector?: string;
  } = {}
): Promise<Locator | null> {
  const { timeout = 30000, listSelector = 'ul[role="list"], table tbody' } = options;
  const deadline = Date.now() + timeout;
  
  // First, poll the API to ensure the item exists
  while (Date.now() < deadline) {
    try {
      const exists = await page.evaluate(async (args) => {
        const resp = await fetch(`/api/admin/${args.entity}?search=${encodeURIComponent(args.query)}&take=100`);
        if (!resp.ok) return false;
        const json = await resp.json();
        const items = Array.isArray(json) ? json : (json.items || []);
        return items.some((item: any) => 
          item.clientId === args.query || 
          item.name === args.query || 
          item.email === args.query
        );
      }, { entity, query: searchQuery });
      
      if (exists) {
        // Item exists in API, now ensure UI is updated
        const searchInput = page.locator('input[placeholder*="Search"], input[type="search"]').first();
        if (await searchInput.isVisible().catch(() => false)) {
          await searchInput.clear();
          await searchInput.fill(searchQuery);
          // Wait for debounce
          await waitForDebounce(page, 600);
          // Wait a bit more for list to update
          await page.waitForTimeout(500);
        }
        
        // Find the item in UI
        const listContainer = page.locator(listSelector);
        const item = listContainer.locator('tr, li', { hasText: searchQuery }).first();
        if (await item.isVisible({ timeout: 5000 }).catch(() => false)) {
          return item;
        }
        
        // If not found in filtered view, try reloading the page to reset state
        await page.reload();
        await page.waitForTimeout(1000);
        if (await searchInput.isVisible().catch(() => false)) {
          await searchInput.fill(searchQuery);
          await waitForDebounce(page, 600);
          await page.waitForTimeout(500);
        }
        const itemAfterReload = listContainer.locator('tr, li', { hasText: searchQuery }).first();
        if (await itemAfterReload.isVisible({ timeout: 5000 }).catch(() => false)) {
          return itemAfterReload;
        }
      }
    } catch (e) {
      // Continue polling
    }
    await page.waitForTimeout(500);
  }
  
  return null;
}

/**
 * Wait for a modal form to be fully ready for interaction.
 * Ensures the form is visible, not animating, and inputs are enabled.
 */
export async function waitForModalFormReady(
  page: Page,
  formSelector: string = 'form',
  options: {
    timeout?: number;
  } = {}
): Promise<boolean> {
  const { timeout = 5000 } = options;
  
  try {
    // Wait for form to be visible
    await page.waitForSelector(formSelector, { timeout, state: 'visible' });
    
    // Wait for any CSS transitions to complete (modals often have fade-in animations)
    await page.waitForTimeout(300);
    
    // Ensure at least one input is enabled and ready
    const form = page.locator(formSelector);
    await form.locator('input:not([disabled]), textarea:not([disabled])').first().waitFor({ 
      timeout: 2000,
      state: 'visible' 
    });
    
    return true;
  } catch (e) {
    return false;
  }
}

/**
 * Wait for input debounce to complete.
 * Use this after filling search inputs that have debounced event handlers.
 */
export async function waitForDebounce(page: Page, ms: number = 500): Promise<void> {
  await page.waitForTimeout(ms);
}

/**
 * Wait for a network response with retry logic.
 * More reliable than waitForResponse when dealing with flaky networks.
 */
export async function waitForApiResponse(
  page: Page,
  urlPattern: string | RegExp,
  options: {
    method?: string;
    timeout?: number;
    status?: number;
  } = {}
): Promise<any> {
  const { method = 'GET', timeout = 10000, status = 200 } = options;
  
  const response = await page.waitForResponse(
    (resp) => {
      const urlMatches = typeof urlPattern === 'string' 
        ? resp.url().includes(urlPattern)
        : urlPattern.test(resp.url());
      const methodMatches = resp.request().method() === method;
      return urlMatches && methodMatches;
    },
    { timeout }
  );
  
  if (response.status() !== status && response.status() !== 201) {
    throw new Error(`Expected status ${status}, got ${response.status()}`);
  }
  
  return response.json();
}

/**
 * Wait for element to be stable (not moving/resizing).
 * Useful for elements that animate in.
 */
export async function waitForElementStable(
  locator: Locator,
  options: {
    timeout?: number;
    stableTime?: number;
  } = {}
): Promise<void> {
  const { timeout = 5000, stableTime = 300 } = options;
  const deadline = Date.now() + timeout;
  
  let lastBox: any = null;
  let stableStart: number | null = null;
  
  while (Date.now() < deadline) {
    try {
      const box = await locator.boundingBox();
      if (!box) {
        await locator.page().waitForTimeout(100);
        continue;
      }
      
      if (lastBox && 
          box.x === lastBox.x && 
          box.y === lastBox.y && 
          box.width === lastBox.width && 
          box.height === lastBox.height) {
        if (!stableStart) {
          stableStart = Date.now();
        } else if (Date.now() - stableStart >= stableTime) {
          return; // Element is stable
        }
      } else {
        stableStart = null;
      }
      
      lastBox = box;
      await locator.page().waitForTimeout(50);
    } catch (e) {
      await locator.page().waitForTimeout(100);
    }
  }
  
  throw new Error('Element did not stabilize within timeout');
}

export default {
  waitForListItemWithRetry,
  waitForModalFormReady,
  waitForDebounce,
  waitForApiResponse,
  waitForElementStable
};
