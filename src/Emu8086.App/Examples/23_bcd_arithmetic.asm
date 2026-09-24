; Packed BCD: each nibble holds a decimal digit. DAA fixes AL after ADD.
#make_COM#
org 100h

    mov al, 38h             ; BCD 38
    add al, 45h             ; binary result 7Dh
    daa                     ; decimal adjust -> 83h = BCD 83
    call print_bcd
    ret

; prints AL as two decimal digits
print_bcd:
    mov ah, al
    shr al, 4               ; high digit
    add al, '0'
    mov dl, al
    mov al, ah
    and al, 0Fh             ; low digit
    add al, '0'
    mov dh, al
    mov ah, 02h
    int 21h                 ; DL
    mov dl, dh
    int 21h
    ret
