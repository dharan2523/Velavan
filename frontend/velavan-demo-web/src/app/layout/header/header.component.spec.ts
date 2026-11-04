import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import {
  ActivatedRoute,
  Data,
  NavigationEnd,
  Router,
  RouterState
} from '@angular/router';
import { Subject } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { HeaderComponent } from './header.component';

describe('HeaderComponent', () => {
  let fixture: ComponentFixture<HeaderComponent>;
  let component: HeaderComponent;
  let events: Subject<unknown>;
  let rootRoute: ActivatedRoute;

  const createRoute = (
    data: Data = {},
    firstChild: ActivatedRoute | null = null
  ): ActivatedRoute =>
    ({ snapshot: { data }, firstChild }) as unknown as ActivatedRoute;

  const createComponent = (root: ActivatedRoute): void => {
    rootRoute = root;
    fixture = TestBed.createComponent(HeaderComponent);
    component = fixture.componentInstance;
  };

  beforeEach(() => {
    events = new Subject<unknown>();
    rootRoute = createRoute();

    const routerState = {
      get root() {
        return rootRoute;
      }
    } as RouterState;
    const router = {
      events: events.asObservable(),
      routerState
    } as unknown as Router;
    const auth = {
      isAuthenticated: signal(false),
      displayName: signal('Test User'),
      initials: signal('TU'),
      primaryRole: signal('User'),
      logout: jasmine.createSpy('logout')
    } as unknown as AuthService;

    TestBed.configureTestingModule({
      imports: [HeaderComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: auth },
        {
          provide: MatDialog,
          useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open'])
        },
        {
          provide: MatSnackBar,
          useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open'])
        }
      ]
    });
  });

  it('uses the Dashboard route title', () => {
    createComponent(
      createRoute({}, createRoute({}, createRoute({ title: 'Dashboard' })))
    );

    expect(component.pageTitle()).toBe('Dashboard');
  });

  it('updates the title on NavigationEnd', () => {
    createComponent(
      createRoute({}, createRoute({}, createRoute({ title: 'Dashboard' })))
    );

    rootRoute = createRoute({}, createRoute({}, createRoute({ title: 'Users' })));
    events.next(new NavigationEnd(1, '/users', '/users'));

    expect(component.pageTitle()).toBe('Users');
  });

  it('uses the deepest title in nested child routes', () => {
    createComponent(
      createRoute(
        {},
        createRoute(
          {},
          createRoute({ title: 'Users' }, createRoute({ title: 'User details' }))
        )
      )
    );

    expect(component.pageTitle()).toBe('User details');
  });

  it('falls back to Dashboard when no title metadata exists', () => {
    createComponent(createRoute({}, createRoute({})));

    expect(component.pageTitle()).toBe('Dashboard');
  });

  it('does not fail when a route snapshot is unavailable during initialization', () => {
    const routeWithoutSnapshot = {
      firstChild: null
    } as unknown as ActivatedRoute;

    createComponent(createRoute({}, routeWithoutSnapshot));

    expect(component.pageTitle()).toBe('Dashboard');
  });
});
